# 統一制約グラフ エンジン F1（グラフ中核）実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 自動配置の順序決定を、ad-hoc な「層整列＋競合移動」から、**全制約を「ハード辺(依存＋重なり)＋優先度(グループ→現在順)」にして1回安定トポロジカルソートする統一グラフ**へ作り替える。

**Architecture:** 安定トポロジカルソート（Kahn法）を、明示的な辺集合＋(グループrank, 現在順)優先度で動くよう一般化（`LoadOrderSorter.SortByEdges`）。重なり辺は「触る資源が多いMOD→少ないMOD」で生成（`OverlapEdges`）。新 `ConstraintGraphOptimizer` が 依存辺＋循環回避した重なり辺 を集めてソートし、既存 `OptimizeResult`（順序＋配置理由）を返す。`MainViewModel.RunAutoSort` を新エンジンへ配線。

**Tech Stack:** C# / .NET 10 / WPF（UIは対象外）/ xUnit。

**設計書:** docs/superpowers/specs/2026-06-25-autosort-unified-graph-engine-design.md（F1＝グラフ中核。F2＝冗長MOD検出は別計画）。

## Global Constraints

- ランタイム NuGet パッケージの追加禁止（`System.Text.Json` のみ可）。テスト用 xUnit は可。
- 実行時に外部AI/Claude を呼ばない（オフライン・ルールのみ）。
- **一般ルールのみ。MOD名・特定ゲームのハードコード／個別分岐は禁止**（依存数・資源数・資源キー一致・グループrankだけで決める）。
- ユーザーは並び替えしない・できない。"要確認" を出さない（必ず決め切る）。
- 既存流儀（static クラス＋record、日本語コメント、`StringComparer.OrdinalIgnoreCase`）。
- ビルド: `dotnet build reloaded-helper.slnx` / テスト: `dotnet test` / 個別: `dotnet test --filter "FullyQualifiedName~Class.Method"`。
- コミットは Claude Code 経由（pre-commit-guard がビルド・テストゲートを発火）。
- 検証は**実機ダンプ**（`tests/ReloadedHelper.Core.Tests/_RealInstallDump.cs`、実 `C:\FreeSoft\Reloaded-II`）を必須ゲートにする。合成テスト緑だけで「完了」としない。

## File Structure

| ファイル | 責務 | 区分 |
|---|---|---|
| `src/ReloadedHelper.Core/LoadOrderSorter.cs` | `SortByEdges` 追加（辺集合＋(rank,index)優先度の安定トポロジカルソート） | 変更 |
| `src/ReloadedHelper.Core/OverlapEdges.cs` | 資源重なり対 →「多い→少ない」辺の生成 | 新規 |
| `src/ReloadedHelper.Core/ConstraintGraphOptimizer.cs` | 依存辺＋循環回避した重なり辺を集め、グループ優先度でソートし `OptimizeResult` を返す | 新規 |
| `src/ReloadedHelper.Core/MainViewModel.cs` | `RunAutoSort` を新エンジンへ配線 | 変更 |
| `tests/.../LoadOrderSorterByEdgesTests.cs` | Task1 | 新規 |
| `tests/.../OverlapEdgesTests.cs` | Task2 | 新規 |
| `tests/.../ConstraintGraphOptimizerTests.cs` | Task3 | 新規 |
| `tests/.../AutoSortWiringTests.cs` | Task4 で配線更新 | 変更 |

`OptimizeResult` / `PlacementReason` / `ModPlacement`（`LoadOrderOptimizer.cs` 内）は再利用する。旧 `LoadOrderOptimizer.Optimize` は Task4 で呼ばれなくなるが、削除は F2 以降（本計画では温存し参照を切り替えるだけ）。

---

## Task 1: 辺集合で動く安定トポロジカルソート（SortByEdges）

**Files:**
- Modify: `src/ReloadedHelper.Core/LoadOrderSorter.cs`（メソッド追加。既存 `Sort` は残す）
- Test: `tests/ReloadedHelper.Core.Tests/LoadOrderSorterByEdgesTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  static IReadOnlyList<string> LoadOrderSorter.SortByEdges(
      IReadOnlyList<string> currentOrder,
      IReadOnlyCollection<(string Before, string After)> edges,
      IReadOnlyDictionary<string, int> groupRankOf)
  ```
  「Before は After より前」を全て満たし、自由な部分は (groupRank昇順 → 現在順index昇順) で並べる。`currentOrder` に無いノードを含む辺は無視。循環に巻き込まれたノードは末尾へ現在順で残す。

- [ ] **Step 1: 失敗するテストを書く**

`tests/ReloadedHelper.Core.Tests/LoadOrderSorterByEdgesTests.cs`:

```csharp
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class LoadOrderSorterByEdgesTests
{
    private static Dictionary<string, int> Ranks(params (string id, int r)[] xs)
        => xs.ToDictionary(x => x.id, x => x.r, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void NoEdges_GroupRankOrders_ThenCurrentOrder()
    {
        // 辺なし。rank昇順（土台=0が前）。同rankは現在順。
        var order = new[] { "vis", "base1", "base2" };
        var ranks = Ranks(("vis", 7), ("base1", 0), ("base2", 0));
        var res = LoadOrderSorter.SortByEdges(order, System.Array.Empty<(string, string)>(), ranks);
        Assert.Equal(new[] { "base1", "base2", "vis" }, res);
    }

    [Fact]
    public void NoEdges_SameRank_KeepsCurrentOrder()
    {
        var order = new[] { "b", "a", "c" };
        var ranks = Ranks(("a", 5), ("b", 5), ("c", 5));
        var res = LoadOrderSorter.SortByEdges(order, System.Array.Empty<(string, string)>(), ranks);
        Assert.Equal(new[] { "b", "a", "c" }, res);
    }

    [Fact]
    public void HardEdge_OverridesGroupRank()
    {
        // 辺 a→b は rank に逆らってでも守る（a が前）。
        var order = new[] { "a", "b" };
        var ranks = Ranks(("a", 9), ("b", 0)); // rankだけなら b が前
        var res = LoadOrderSorter.SortByEdges(order, new[] { ("a", "b") }, ranks);
        Assert.Equal(new[] { "a", "b" }, res);
    }

    [Fact]
    public void Edge_MovesAfterNode_Later()
    {
        var order = new[] { "x", "y", "z" };
        var ranks = Ranks(("x", 0), ("y", 0), ("z", 0));
        // y は z より後（z→y）。
        var res = LoadOrderSorter.SortByEdges(order, new[] { ("z", "y") }, ranks);
        Assert.True(res.ToList().IndexOf("y") > res.ToList().IndexOf("z"));
    }

    [Fact]
    public void Cycle_RemainderAppendedInCurrentOrder()
    {
        var order = new[] { "a", "b" };
        var ranks = Ranks(("a", 0), ("b", 0));
        var res = LoadOrderSorter.SortByEdges(order, new[] { ("a", "b"), ("b", "a") }, ranks);
        Assert.Equal(2, res.Count);
        Assert.Contains("a", res);
        Assert.Contains("b", res);
    }

    [Fact]
    public void UnknownNodeInEdge_Ignored()
    {
        var order = new[] { "a", "b" };
        var ranks = Ranks(("a", 0), ("b", 0));
        var res = LoadOrderSorter.SortByEdges(order, new[] { ("ghost", "a") }, ranks);
        Assert.Equal(new[] { "a", "b" }, res);
    }
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~LoadOrderSorterByEdgesTests"`
Expected: コンパイルエラー（`SortByEdges` 未定義）。

- [ ] **Step 3: 実装する**

`src/ReloadedHelper.Core/LoadOrderSorter.cs` の `class LoadOrderSorter` 内（既存 `Sort` の下）に追加：

```csharp
    /// <summary>
    /// 「Before は After より前」を全て満たし、自由な部分は (groupRank昇順 → 現在順index昇順) で並べる。
    /// currentOrder に無いノードを含む辺は無視。循環に巻き込まれたノードは末尾へ現在順で残す。
    /// </summary>
    public static IReadOnlyList<string> SortByEdges(
        IReadOnlyList<string> currentOrder,
        IReadOnlyCollection<(string Before, string After)> edges,
        IReadOnlyDictionary<string, int> groupRankOf)
    {
        var present = new HashSet<string>(currentOrder, StringComparer.OrdinalIgnoreCase);
        var index = currentOrder
            .Select((id, i) => (id, i))
            .ToDictionary(x => x.id, x => x.i, StringComparer.OrdinalIgnoreCase);

        var afters = currentOrder.ToDictionary(
            m => m, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        var inDegree = currentOrder.ToDictionary(
            m => m, _ => 0, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (before, after) in edges)
        {
            if (!present.Contains(before) || !present.Contains(after)) continue;
            if (string.Equals(before, after, StringComparison.OrdinalIgnoreCase)) continue;
            var key = $"{before.ToLowerInvariant()}{after.ToLowerInvariant()}";
            if (!seen.Add(key)) continue;
            afters[before].Add(after);
            inDegree[after]++;
        }

        int Rank(string id) => groupRankOf.TryGetValue(id, out var r) ? r : int.MaxValue;
        var comparer = Comparer<string>.Create((x, y) =>
        {
            int c = Rank(x).CompareTo(Rank(y));
            return c != 0 ? c : index[x].CompareTo(index[y]);
        });

        var ready = new SortedSet<string>(comparer);
        foreach (var m in currentOrder)
            if (inDegree[m] == 0) ready.Add(m);

        var result = new List<string>(currentOrder.Count);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (ready.Count > 0)
        {
            var m = ready.Min!;
            ready.Remove(m);
            result.Add(m);
            visited.Add(m);
            foreach (var a in afters[m])
                if (--inDegree[a] == 0) ready.Add(a);
        }

        // 循環に巻き込まれて出られなかったノードは現在順で末尾へ。
        foreach (var m in currentOrder)
            if (!visited.Contains(m)) result.Add(m);

        return result;
    }
```

- [ ] **Step 4: テスト成功を確認**

Run: `dotnet test --filter "FullyQualifiedName~LoadOrderSorterByEdgesTests"` → PASS（6件）。
Run: `dotnet test --filter "FullyQualifiedName~LoadOrderSorterTests"` → 既存も緑。

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/LoadOrderSorter.cs tests/ReloadedHelper.Core.Tests/LoadOrderSorterByEdgesTests.cs
git commit -m "feat: LoadOrderSorter.SortByEdges（辺集合＋グループ優先度の安定トポロジカルソート）"
```

---

## Task 2: 重なり辺の生成（OverlapEdges）

**Files:**
- Create: `src/ReloadedHelper.Core/OverlapEdges.cs`
- Test: `tests/ReloadedHelper.Core.Tests/OverlapEdgesTests.cs`

**Interfaces:**
- Consumes: `FileConflict`（既存 record：`PathKey`, `IReadOnlyList<string> ModIds`, `WinnerModId`）
- Produces:
  ```csharp
  static IReadOnlyList<(string Before, string After)> OverlapEdges.Build(
      IReadOnlyList<FileConflict> conflicts,
      IReadOnlyDictionary<string, int> resourceCountByMod)
  ```
  同じ資源を触る各MOD対について「触る資源数が多い方 → 少ない方」の辺。数が同じ対は辺なし。同一対は重複排除。

- [ ] **Step 1: 失敗するテストを書く**

`tests/ReloadedHelper.Core.Tests/OverlapEdgesTests.cs`:

```csharp
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class OverlapEdgesTests
{
    private static Dictionary<string, int> Counts(params (string id, int n)[] xs)
        => xs.ToDictionary(x => x.id, x => x.n, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void MoreResources_PointsTo_Fewer()
    {
        // big(100) と small(1) が file:a で重なる → big→small（small が後＝勝ち）。
        var conflicts = new[] { new FileConflict("file:a", new[] { "small", "big" }, "big") };
        var edges = OverlapEdges.Build(conflicts, Counts(("small", 1), ("big", 100)));
        Assert.Contains(("big", "small"), edges);
        Assert.Single(edges);
    }

    [Fact]
    public void EqualResources_NoEdge()
    {
        var conflicts = new[] { new FileConflict("file:a", new[] { "a", "b" }, "b") };
        var edges = OverlapEdges.Build(conflicts, Counts(("a", 5), ("b", 5)));
        Assert.Empty(edges);
    }

    [Fact]
    public void SamePairAcrossManyResources_Deduped()
    {
        var conflicts = new[]
        {
            new FileConflict("file:a", new[] { "small", "big" }, "big"),
            new FileConflict("file:b", new[] { "small", "big" }, "big"),
        };
        var edges = OverlapEdges.Build(conflicts, Counts(("small", 1), ("big", 100)));
        Assert.Single(edges);
        Assert.Contains(("big", "small"), edges);
    }

    [Fact]
    public void ThreeWayConflict_AllPairs()
    {
        // a(10),b(5),c(1) が同じ資源で重なる → a→b, a→c, b→c。
        var conflicts = new[] { new FileConflict("file:a", new[] { "a", "b", "c" }, "c") };
        var edges = OverlapEdges.Build(conflicts, Counts(("a", 10), ("b", 5), ("c", 1)));
        Assert.Contains(("a", "b"), edges);
        Assert.Contains(("a", "c"), edges);
        Assert.Contains(("b", "c"), edges);
        Assert.Equal(3, edges.Count);
    }

    [Fact]
    public void MissingCount_TreatedAsZero_SoKnownMoreWins()
    {
        // big に count あり、unk は未知(=0扱い) → big(5) > unk(0) → big→unk。
        var conflicts = new[] { new FileConflict("file:a", new[] { "big", "unk" }, "unk") };
        var edges = OverlapEdges.Build(conflicts, Counts(("big", 5)));
        Assert.Contains(("big", "unk"), edges);
    }
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~OverlapEdgesTests"`
Expected: コンパイルエラー（`OverlapEdges` 未定義）。

- [ ] **Step 3: 実装する**

`src/ReloadedHelper.Core/OverlapEdges.cs`:

```csharp
namespace ReloadedHelper.Core;

// 同じ資源を触るMOD対から「触る資源が多い方 → 少ない方」の辺を作る。
// 少ない方が後＝勝ち（狙い撃ちの小さいMODが、広く触る大きいMODに勝つ＝LOOTの重なり則）。
public static class OverlapEdges
{
    public static IReadOnlyList<(string Before, string After)> Build(
        IReadOnlyList<FileConflict> conflicts,
        IReadOnlyDictionary<string, int> resourceCountByMod)
    {
        int Count(string id) => resourceCountByMod.TryGetValue(id, out var n) ? n : 0;

        var result = new List<(string Before, string After)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var c in conflicts)
        {
            var mods = c.ModIds;
            for (int i = 0; i < mods.Count; i++)
                for (int j = i + 1; j < mods.Count; j++)
                {
                    var a = mods[i];
                    var b = mods[j];
                    if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) continue;

                    int ca = Count(a), cb = Count(b);
                    if (ca == cb) continue; // 同数 → 辺なし（タイブレークに委ねる）

                    var (before, after) = ca > cb ? (a, b) : (b, a); // 多い→少ない
                    var key = $"{before.ToLowerInvariant()}{after.ToLowerInvariant()}";
                    if (seen.Add(key)) result.Add((before, after));
                }
        }
        return result;
    }
}
```

- [ ] **Step 4: テスト成功を確認**

Run: `dotnet test --filter "FullyQualifiedName~OverlapEdgesTests"` → PASS（5件）。

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/OverlapEdges.cs tests/ReloadedHelper.Core.Tests/OverlapEdgesTests.cs
git commit -m "feat: OverlapEdges（重なり対→多い方が前・少ない方が勝ちの辺）"
```

---

## Task 3: 統一グラフ オプティマイザ（ConstraintGraphOptimizer）

**Files:**
- Create: `src/ReloadedHelper.Core/ConstraintGraphOptimizer.cs`
- Test: `tests/ReloadedHelper.Core.Tests/ConstraintGraphOptimizerTests.cs`

**Interfaces:**
- Consumes: `LoadOrderSorter.SortByEdges`（Task1）、`OverlapEdges.Build`（Task2）、`OptimizeResult`/`ModPlacement`/`PlacementReason`（既存 `LoadOrderOptimizer.cs`）、`ModType`/`ModTypeInfo`（既存）。
- Produces:
  ```csharp
  static OptimizeResult ConstraintGraphOptimizer.Optimize(
      IReadOnlyList<string> currentOrder,
      IReadOnlyDictionary<string, IReadOnlyList<string>> dependenciesOf,
      IReadOnlyList<FileConflict> conflicts,
      IReadOnlyDictionary<string, ModType> typesByMod,
      IReadOnlyDictionary<string, int> resourceCountByMod,
      IReadOnlyDictionary<string, string>? typeReasons = null)
  ```
  依存辺＋（依存と循環しない）重なり辺を集め、(グループrank, 現在順) 優先度で `SortByEdges`。`OptimizeResult.Unresolved` は常に空。`Placements` は最終順序＋種類ラベル/理由。

**実装方針（循環回避）:** 依存辺はハード。重なり辺 `more→fewer` は、**依存だけで既に `fewer → ... → more` が成り立つ場合のみ捨てる**（依存が勝つ）。依存の到達可能性は依存辺のみで一度だけ計算（DFS）。重なり辺どうしは資源数の単調減少なので循環しない。

- [ ] **Step 1: 失敗するテストを書く**

`tests/ReloadedHelper.Core.Tests/ConstraintGraphOptimizerTests.cs`:

```csharp
using ReloadedHelper.Core;
using Xunit;

namespace ReloadedHelper.Core.Tests;

public class ConstraintGraphOptimizerTests
{
    private static readonly Dictionary<string, IReadOnlyList<string>> NoDeps = new();
    private static IReadOnlyDictionary<string, ModType> Types(params (string id, ModType t)[] xs)
        => xs.ToDictionary(x => x.id, x => x.t, StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, int> Counts(params (string id, int n)[] xs)
        => xs.ToDictionary(x => x.id, x => x.n, StringComparer.OrdinalIgnoreCase);
    private static int Idx(OptimizeResult r, string id) => r.Order.ToList().FindIndex(x => x == id);

    [Fact]
    public void Framework_GoesFirst_ByGroupRank()
    {
        // lib(Library=rank0) は現在順で最後でも、グループ優先度で前へ。競合なし。
        var order = new[] { "skin", "lib" };
        var types = Types(("skin", ModType.SkinTexture), ("lib", ModType.Library));
        var r = ConstraintGraphOptimizer.Optimize(order, NoDeps,
            System.Array.Empty<FileConflict>(), types, Counts(("skin", 5), ("lib", 0)));
        Assert.True(Idx(r, "lib") < Idx(r, "skin"));
        Assert.Empty(r.Unresolved);
    }

    [Fact]
    public void Conflict_FewerResources_Wins_EvenSameGroup()
    {
        // 同グループ。big(100) と small(1) が競合 → small が後＝勝ち。
        var order = new[] { "small", "big" };
        var types = Types(("small", ModType.SkinTexture), ("big", ModType.SkinTexture));
        var conflicts = new[] { new FileConflict("file:a", new[] { "small", "big" }, "big") };
        var r = ConstraintGraphOptimizer.Optimize(order, NoDeps, conflicts, types,
            Counts(("small", 1), ("big", 100)));
        Assert.True(Idx(r, "small") > Idx(r, "big"));
    }

    [Fact]
    public void Dependency_AlwaysRespected_OverOverlap()
    {
        // big は small に依存（small が前）。重なりは big(100)→small(1) を要求するが、
        // 依存 small→big と矛盾するので重なり辺は捨てる。結果 small が前。
        var order = new[] { "small", "big" };
        var deps = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        { ["big"] = new[] { "small" } };
        var conflicts = new[] { new FileConflict("file:a", new[] { "small", "big" }, "big") };
        var types = Types(("small", ModType.SkinTexture), ("big", ModType.SkinTexture));
        var r = ConstraintGraphOptimizer.Optimize(order, deps, conflicts, types,
            Counts(("small", 1), ("big", 100)));
        Assert.True(Idx(r, "small") < Idx(r, "big"));
        Assert.Empty(r.Unresolved);
    }

    [Fact]
    public void NoConflict_SameGroup_KeepsCurrentOrder()
    {
        var order = new[] { "b", "a" };
        var types = Types(("a", ModType.SkinTexture), ("b", ModType.SkinTexture));
        var r = ConstraintGraphOptimizer.Optimize(order, NoDeps,
            System.Array.Empty<FileConflict>(), types, Counts(("a", 3), ("b", 3)));
        Assert.Equal(new[] { "b", "a" }, r.Order);
    }

    [Fact]
    public void Placements_CarryTypeLabelAndReason()
    {
        var order = new[] { "a" };
        var types = Types(("a", ModType.SkinTexture));
        var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { ["a"] = "名前からスキン・テクスチャと判定" };
        var r = ConstraintGraphOptimizer.Optimize(order, NoDeps,
            System.Array.Empty<FileConflict>(), types, Counts(("a", 1)), reasons);
        Assert.Single(r.Placements);
        Assert.Equal("名前からスキン・テクスチャと判定", r.Placements[0].Reason);
        Assert.Equal(ModTypeInfo.Label(ModType.SkinTexture), r.Placements[0].LayerLabel);
    }
}
```

- [ ] **Step 2: 失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~ConstraintGraphOptimizerTests"`
Expected: コンパイルエラー（`ConstraintGraphOptimizer` 未定義）。

- [ ] **Step 3: 実装する**

`src/ReloadedHelper.Core/ConstraintGraphOptimizer.cs`:

```csharp
namespace ReloadedHelper.Core;

// 統一制約グラフ：依存辺(ハード)＋重なり辺(ハード・依存と循環するものは捨てる)を集め、
// (グループrank昇順 → 現在順) 優先度で安定トポロジカルソート。Unresolved は出さない。
public static class ConstraintGraphOptimizer
{
    public static OptimizeResult Optimize(
        IReadOnlyList<string> currentOrder,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependenciesOf,
        IReadOnlyList<FileConflict> conflicts,
        IReadOnlyDictionary<string, ModType> typesByMod,
        IReadOnlyDictionary<string, int> resourceCountByMod,
        IReadOnlyDictionary<string, string>? typeReasons = null)
    {
        var present = new HashSet<string>(currentOrder, StringComparer.OrdinalIgnoreCase);

        // 1. 依存辺（dep → mod）。currentOrder にあるもののみ。
        var depEdges = new List<(string Before, string After)>();
        var depAdj = currentOrder.ToDictionary(m => m, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var mod in currentOrder)
            if (dependenciesOf.TryGetValue(mod, out var deps))
                foreach (var dep in deps)
                    if (present.Contains(dep) && !string.Equals(dep, mod, StringComparison.OrdinalIgnoreCase))
                    {
                        depEdges.Add((dep, mod));
                        depAdj[dep].Add(mod);
                    }

        // 2. 依存到達可能性: depReaches(x, y) = 依存辺だけで x から y へ行けるか。
        bool DepReaches(string from, string to)
        {
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return true;
            var stack = new Stack<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            stack.Push(from);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (!seen.Add(cur)) continue;
                if (string.Equals(cur, to, StringComparison.OrdinalIgnoreCase)) return true;
                if (depAdj.TryGetValue(cur, out var nexts))
                    foreach (var n in nexts) stack.Push(n);
            }
            return false;
        }

        // 3. 重なり辺（more→fewer）。依存が逆（fewer→more）を強制しているものは捨てる。
        var overlap = OverlapEdges.Build(conflicts, resourceCountByMod);
        var edges = new List<(string Before, string After)>(depEdges);
        foreach (var (before, after) in overlap)
        {
            if (!present.Contains(before) || !present.Contains(after)) continue;
            if (DepReaches(after, before)) continue; // 依存が after→before を強制＝矛盾 → 捨てる
            edges.Add((before, after));
        }

        // 4. グループ優先度（rank）でソート。
        var ranks = currentOrder.ToDictionary(
            id => id,
            id => ModTypeInfo.Rank(typesByMod.GetValueOrDefault(id, ModType.Unknown)),
            StringComparer.OrdinalIgnoreCase);
        var order = LoadOrderSorter.SortByEdges(currentOrder, edges, ranks).ToList();

        // 5. 配置（種類ラベル＋理由）。
        var placements = order.Select(id =>
        {
            var type = typesByMod.GetValueOrDefault(id, ModType.Unknown);
            var reason = typeReasons != null && typeReasons.TryGetValue(id, out var rs) && !string.IsNullOrWhiteSpace(rs)
                ? rs
                : $"{ModTypeInfo.Label(type)}として配置";
            return new ModPlacement(id, ModTypeInfo.Rank(type), ModTypeInfo.Label(type), reason);
        }).ToList();

        return new OptimizeResult(order, System.Array.Empty<PlacementReason>(),
            System.Array.Empty<(string A, string B)>(), placements);
    }
}
```

- [ ] **Step 4: テスト成功を確認**

Run: `dotnet test --filter "FullyQualifiedName~ConstraintGraphOptimizerTests"` → PASS（5件）。
Run: `dotnet build reloaded-helper.slnx` → 0 errors。

- [ ] **Step 5: コミット**

```bash
git add src/ReloadedHelper.Core/ConstraintGraphOptimizer.cs tests/ReloadedHelper.Core.Tests/ConstraintGraphOptimizerTests.cs
git commit -m "feat: ConstraintGraphOptimizer（依存＋重なり辺をグループ優先度で統一ソート）"
```

---

## Task 4: MainViewModel を新エンジンへ配線＋実機検証

**Files:**
- Modify: `src/ReloadedHelper.Core/MainViewModel.cs`（`RunAutoSort` の Optimize 呼び出しを差し替え）
- Test: `tests/ReloadedHelper.Core.Tests/AutoSortWiringTests.cs`（必要なら更新）
- Verify: `tests/ReloadedHelper.Core.Tests/_RealInstallDump.cs`（実機ダンプ：最終順序も出力するよう拡張）

**Interfaces:**
- Consumes: `ConstraintGraphOptimizer.Optimize`（Task3）

- [ ] **Step 1: RunAutoSort の呼び出しを差し替える**

`src/ReloadedHelper.Core/MainViewModel.cs` の `RunAutoSort` 内、現在の
`var result = LoadOrderOptimizer.Optimize(appId, game.SortedMods, depMap, diagResult.Conflicts, types, _prefs, typeReasons, resourceCount, hints);`
を以下へ置換：

```csharp
        var result = ConstraintGraphOptimizer.Optimize(
            game.SortedMods, depMap, diagResult.Conflicts, types, resourceCount, typeReasons);
```

> `_prefs` と `hints` は新エンジンでは使わない（順序は決め切る／作者指示は重なり則に包含される思想）。`hints`/`BuildHints` が他で未使用になるなら警告が出るので、その場合は `BuildHints` 呼び出し行とメソッドを削除（未使用 using も整理）。`_prefs` フィールドは F2(冗長却下記憶) で使うため残す。

- [ ] **Step 2: ビルドして未使用を解消**

Run: `dotnet build reloaded-helper.slnx`
Expected: 0 errors。未使用警告（`hints`/`BuildHints`/`PlacementHintParser` 等）が出たら、未使用になったローカル・privateメソッドのみ削除して解消（public API は触らない）。

- [ ] **Step 3: 既存テストを通す**

Run: `dotnet test --filter "FullyQualifiedName~AutoSortWiringTests"` → PASS（`BuildTypeDecisions` の呼び出しは不変のはず。落ちたら新エンジンの呼び出しに合わせて期待を更新）。
Run: `dotnet test`（`_RealInstallDump` 除く）→ 全件 PASS。

- [ ] **Step 4: 実機ダンプを「最終順序」も出すよう拡張**

`tests/ReloadedHelper.Core.Tests/_RealInstallDump.cs` に、`ConstraintGraphOptimizer.Optimize` を実 install の `game.SortedMods`＋実 deps＋実 conflicts＋types＋resourceCount で呼び、結果順序の先頭20件と「土台(Library)が全て前方に固まっているか」をダンプ末尾に追記する。最小追記例：

```csharp
        // --- 最終順序の検証（新エンジン） ---
        var depMap = catalog.ToDictionary(
            kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value.Dependencies, System.StringComparer.OrdinalIgnoreCase);
        var typeMap = rows.ToDictionary(x => x.id, x => x.type, System.StringComparer.OrdinalIgnoreCase);
        var resCount = diag.Resources.ToDictionary(r => r.ModId, r => r.Resources.Count, System.StringComparer.OrdinalIgnoreCase);
        var opt = ConstraintGraphOptimizer.Optimize(game.SortedMods, depMap, diag.Conflicts, typeMap, resCount);
        sb.AppendLine();
        sb.AppendLine("--- 新エンジン最終順序 先頭25件 ---");
        foreach (var id in opt.Order.Where(enabled.Contains).Take(25))
            sb.AppendLine($"  {(typeMap.TryGetValue(id, out var t) ? ModTypeInfo.Label(t) : "?"),-16} {id}");
        File.WriteAllText(Out, sb.ToString());
```

（`rows` は既存のダンプ行。`typeMap` は有効MODのみ。`opt.Order` 全体には無効MODも含まれるので `enabled.Contains` で絞って表示。）

- [ ] **Step 5: 実機ダンプ実行＋目視**

Run: `dotnet test --filter "FullyQualifiedName~_RealInstallDump"`
Read: `classification-dump.txt` の「新エンジン最終順序 先頭25件」。**土台(Library)系（modloader/crifs/各FW）が先頭に固まり、音楽・見た目が後方**であることを確認。明らかな破綻（FWが後方、依存崩れ）が無いこと。

- [ ] **Step 6: 全体ビルド・テスト・コミット**

Run: `dotnet build reloaded-helper.slnx` → 0 errors。`dotnet test`（_RealInstallDump 含む）→ 全緑。
```bash
git add src/ReloadedHelper.Core/MainViewModel.cs tests/ReloadedHelper.Core.Tests/AutoSortWiringTests.cs
git commit -m "feat: RunAutoSort を統一制約グラフエンジン(ConstraintGraphOptimizer)へ配線"
```
（`_RealInstallDump.cs` と `classification-dump.txt` はコミットしない＝検証用ローカル。）

---

## 完了の定義（F1）

- 順序が「依存(ハード)＋重なり(ハード・依存に劣後)＋グループ優先度＋現在順安定」の統一グラフ1回ソートで決まる。
- 土台が確実に前方、競合は「少数派が勝つ」で自動裁定、無関係MODは動かない。
- 全合成テスト緑 ＋ **実機ダンプで最終順序が妥当**（土台が前方に固まる）。
- 次（F2）：冗長MOD検出＋診断提示＋却下記憶は別計画。

## F1スコープ外（後段）

- F2：冗長MOD検出（Vortex の redundant 相当）と診断パネル提示・`PreferenceStore` 却下記憶。
- 「効いてる証拠」予告UI・起動時ログ/クラッシュ検知。
- 旧 `LoadOrderOptimizer` 完全撤去（F1では温存し参照のみ切替）。`PlacementHintParser`/`WinnerResolver` の去就も後段で整理。
