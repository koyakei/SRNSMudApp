-- ファイル: Scripts/Migrate_Tag_HierarchyId.sql
-- 目的: ParentTagId (隣接リスト) → Node (hierarchyid) へのデータ移行
-- 前提: AddTagHierarchyId マイグレーション適用済みであること

WITH RecursiveCTE AS (
    -- ベースケース: ルートタグ (ParentTagId IS NULL)
    SELECT
        t.Id,
        t.ParentTagId,
        CAST(
            hierarchyid::GetRoot().GetDescendant(
                (
                    SELECT MAX(t2.Node)
                    FROM dbo.Tags t2
                    WHERE t2.ParentTagId IS NULL
                      AND t2.Id < t.Id
                ),
                NULL
            ) AS hierarchyid
        ) AS ComputedNode,
        0 AS Depth
    FROM dbo.Tags t
    WHERE t.ParentTagId IS NULL

    UNION ALL

    -- 再帰ケース: 子タグ
    SELECT
        child.Id,
        child.ParentTagId,
        CAST(
            parent.ComputedNode.GetDescendant(
                (
                    SELECT MAX(sibling.Node)
                    FROM dbo.Tags sibling
                    WHERE sibling.ParentTagId = child.ParentTagId
                      AND sibling.Id < child.Id
                ),
                NULL
            ) AS hierarchyid
        ) AS ComputedNode,
        parent.Depth + 1 AS Depth
    FROM dbo.Tags child
    INNER JOIN RecursiveCTE parent ON child.ParentTagId = parent.Id
)
UPDATE t
SET t.Node = cte.ComputedNode
FROM dbo.Tags t
INNER JOIN RecursiveCTE cte ON t.Id = cte.Id;

-- 移行結果の確認クエリ
SELECT Id, Name, ParentTagId, Node.ToString() AS NodePath, Node.GetLevel() AS Level
FROM dbo.Tags
ORDER BY Node;
