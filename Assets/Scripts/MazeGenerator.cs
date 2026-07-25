using System.Collections.Generic;
using UnityEngine;

/// <summary>One wall segment in world space (center + size), ready to become mesh + collider.</summary>
public struct WallSegment
{
    public Vector2 center;
    public Vector2 size;
    public WallSegment(Vector2 c, Vector2 s) { center = c; size = s; }
}

/// <summary>Result of generating a maze: walls, connectivity, and useful world bounds.</summary>
public class MazeData
{
    public int size;
    public float cellSize;
    public List<WallSegment> walls;
    public int[,] cells;                 // per-cell wall bitmask (bit set = wall present)
    public List<Vector2Int> deadEnds;    // cells with 3 walls (excl. start & exit)
    public List<Vector2Int> solutionPath;// unique start->exit path (for on-path decoys)
    public Vector2 startPos;
    public Vector2 exitPos;
    public Vector2Int exitCell;
    public Vector2 worldMin;
    public Vector2 worldMax;
    public Vector2 worldCenter;
    public float worldWidth;
    public float worldHeight;

    public Vector2 CellCenter(int x, int y) => new Vector2(x * cellSize, y * cellSize);

    /// <summary>Is the wall on the given side of cell (x,y) open (carved)?</summary>
    public bool IsOpen(int x, int y, int dirBit) => (cells[x, y] & dirBit) == 0;
}

/// <summary>
/// Recursive-backtracker maze generator. Produces a perfect maze (exactly one path
/// between any two cells), so the exit is always reachable. Pure data — no GameObjects.
/// </summary>
public static class MazeGenerator
{
    public const int N = 1, E = 2, S = 4, W = 8;

    public static MazeData Generate(int size, float cellSize, int seed)
    {
        var rng = new System.Random(seed);

        int[,] cells = new int[size, size];
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
                cells[x, y] = N | E | S | W;

        bool[,] visited = new bool[size, size];
        var stack = new Stack<Vector2Int>();
        var start = new Vector2Int(0, 0);
        visited[start.x, start.y] = true;
        stack.Push(start);

        while (stack.Count > 0)
        {
            var cur = stack.Peek();
            var neighbors = UnvisitedNeighbors(cur, size, visited);
            if (neighbors.Count == 0) { stack.Pop(); continue; }

            var next = neighbors[rng.Next(neighbors.Count)];
            RemoveWallBetween(cells, cur, next);
            visited[next.x, next.y] = true;
            stack.Push(next);
        }

        // Wall segments in world space (emit each shared wall once + outer border).
        var walls = new List<WallSegment>();
        float half = cellSize * 0.5f;
        float t = GameConfig.WallThickness;
        for (int x = 0; x < size; x++)
        for (int y = 0; y < size; y++)
        {
            Vector2 c = new Vector2(x * cellSize, y * cellSize);
            if ((cells[x, y] & E) != 0)
                walls.Add(new WallSegment(c + new Vector2(half, 0f), new Vector2(t, cellSize + t)));
            if ((cells[x, y] & N) != 0)
                walls.Add(new WallSegment(c + new Vector2(0f, half), new Vector2(cellSize + t, t)));
            if (x == 0 && (cells[x, y] & W) != 0)
                walls.Add(new WallSegment(c + new Vector2(-half, 0f), new Vector2(t, cellSize + t)));
            if (y == 0 && (cells[x, y] & S) != 0)
                walls.Add(new WallSegment(c + new Vector2(0f, -half), new Vector2(cellSize + t, t)));
        }

        var exitCell = new Vector2Int(size - 1, size - 1);

        // Dead ends (3 walls) that aren't the start or the exit — good spots for decoys.
        var deadEnds = new List<Vector2Int>();
        for (int x = 0; x < size; x++)
        for (int y = 0; y < size; y++)
        {
            if ((x == 0 && y == 0) || (x == exitCell.x && y == exitCell.y)) continue;
            if (WallCount(cells[x, y]) == 3) deadEnds.Add(new Vector2Int(x, y));
        }

        var solution = SolvePath(cells, size, new Vector2Int(0, 0), exitCell);

        var data = new MazeData
        {
            size = size,
            cellSize = cellSize,
            walls = walls,
            cells = cells,
            deadEnds = deadEnds,
            solutionPath = solution,
            startPos = new Vector2(0f, 0f),
            exitCell = exitCell,
            exitPos = new Vector2(exitCell.x * cellSize, exitCell.y * cellSize),
            worldMin = new Vector2(-half, -half),
            worldMax = new Vector2((size - 1) * cellSize + half, (size - 1) * cellSize + half),
        };
        data.worldWidth = data.worldMax.x - data.worldMin.x;
        data.worldHeight = data.worldMax.y - data.worldMin.y;
        data.worldCenter = (data.worldMin + data.worldMax) * 0.5f;
        return data;
    }

    /// <summary>BFS the (unique) start->exit path through the carved maze.</summary>
    private static List<Vector2Int> SolvePath(int[,] cells, int size, Vector2Int start, Vector2Int exit)
    {
        var prev = new Dictionary<Vector2Int, Vector2Int>();
        var seen = new HashSet<Vector2Int> { start };
        var q = new Queue<Vector2Int>();
        q.Enqueue(start);

        while (q.Count > 0)
        {
            var c = q.Dequeue();
            if (c == exit) break;

            // Step to a neighbor only if the wall between them is carved open.
            TryStep(cells, seen, prev, q, c, new Vector2Int(c.x, c.y + 1), N);
            TryStep(cells, seen, prev, q, c, new Vector2Int(c.x + 1, c.y), E);
            TryStep(cells, seen, prev, q, c, new Vector2Int(c.x, c.y - 1), S);
            TryStep(cells, seen, prev, q, c, new Vector2Int(c.x - 1, c.y), W);
        }

        var path = new List<Vector2Int>();
        var cur = exit;
        path.Add(cur);
        while (cur != start && prev.ContainsKey(cur))
        {
            cur = prev[cur];
            path.Add(cur);
        }
        path.Reverse(); // start -> exit
        return path;
    }

    private static void TryStep(int[,] cells, HashSet<Vector2Int> seen, Dictionary<Vector2Int, Vector2Int> prev,
                                Queue<Vector2Int> q, Vector2Int from, Vector2Int to, int dirBit)
    {
        if ((cells[from.x, from.y] & dirBit) != 0) return; // wall present -> can't pass
        if (seen.Contains(to)) return;
        seen.Add(to);
        prev[to] = from;
        q.Enqueue(to);
    }

    private static int WallCount(int mask)
    {
        int c = 0;
        if ((mask & N) != 0) c++;
        if ((mask & E) != 0) c++;
        if ((mask & S) != 0) c++;
        if ((mask & W) != 0) c++;
        return c;
    }

    private static List<Vector2Int> UnvisitedNeighbors(Vector2Int c, int size, bool[,] visited)
    {
        var list = new List<Vector2Int>(4);
        if (c.y + 1 < size && !visited[c.x, c.y + 1]) list.Add(new Vector2Int(c.x, c.y + 1));
        if (c.x + 1 < size && !visited[c.x + 1, c.y]) list.Add(new Vector2Int(c.x + 1, c.y));
        if (c.y - 1 >= 0 && !visited[c.x, c.y - 1]) list.Add(new Vector2Int(c.x, c.y - 1));
        if (c.x - 1 >= 0 && !visited[c.x - 1, c.y]) list.Add(new Vector2Int(c.x - 1, c.y));
        return list;
    }

    private static void RemoveWallBetween(int[,] cells, Vector2Int a, Vector2Int b)
    {
        int dx = b.x - a.x;
        int dy = b.y - a.y;
        if (dy == 1)      { cells[a.x, a.y] &= ~N; cells[b.x, b.y] &= ~S; }
        else if (dy == -1){ cells[a.x, a.y] &= ~S; cells[b.x, b.y] &= ~N; }
        else if (dx == 1) { cells[a.x, a.y] &= ~E; cells[b.x, b.y] &= ~W; }
        else if (dx == -1){ cells[a.x, a.y] &= ~W; cells[b.x, b.y] &= ~E; }
    }
}
