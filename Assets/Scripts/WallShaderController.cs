using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the shared sonar wall material and builds the maze walls. For performance the
/// walls render as ONE combined mesh (a single draw call) using EchoMaze/SonarWall,
/// while collision uses many lightweight BoxCollider2D on a single static object.
/// Because the mesh is authored in world space at identity transform, the shader's
/// world-space math "just works".
/// </summary>
public class WallShaderController : MonoBehaviour
{
    public Material WallMaterial { get; private set; }

    private GameObject _meshGO;
    private MeshFilter _meshFilter;
    private Mesh _mesh;
    private GameObject _colliderGO;

    // Reused buffers so rebuilding a level doesn't allocate fresh arrays each time.
    private readonly List<Vector3> _verts = new List<Vector3>(2048);
    private readonly List<int> _tris = new List<int>(3072);

    public void Init()
    {
        var shader = Shader.Find("EchoMaze/SonarWall");
        if (shader == null)
            Debug.LogError("[EchoMaze] Shader 'EchoMaze/SonarWall' not found.");

        WallMaterial = new Material(shader) { name = "SonarWallMat" };
        WallMaterial.SetColor("_Color", GameConfig.WallGlowColor);

        _meshGO = new GameObject("WallMesh");
        _meshGO.transform.SetParent(transform, false);
        _meshFilter = _meshGO.AddComponent<MeshFilter>();
        var mr = _meshGO.AddComponent<MeshRenderer>();
        mr.sharedMaterial = WallMaterial;
        mr.sortingOrder = 10;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        _mesh = new Mesh { name = "WallCombinedMesh" };
        _mesh.MarkDynamic();
        _meshFilter.sharedMesh = _mesh;
    }

    /// <summary>Re-tint every wall at once (used by the per-sector palette shift).</summary>
    public void SetGlowColor(Color c)
    {
        if (WallMaterial != null) WallMaterial.SetColor("_Color", c);
    }

    public void Clear()
    {
        if (_colliderGO != null) Destroy(_colliderGO);
        if (_mesh != null) _mesh.Clear();
    }

    public void Build(MazeData maze)
    {
        Clear();

        // ---- Combined render mesh ----
        _verts.Clear();
        _tris.Clear();
        foreach (var seg in maze.walls)
        {
            float hx = seg.size.x * 0.5f;
            float hy = seg.size.y * 0.5f;
            float cx = seg.center.x;
            float cy = seg.center.y;
            int b = _verts.Count;
            _verts.Add(new Vector3(cx - hx, cy - hy, 0f));
            _verts.Add(new Vector3(cx + hx, cy - hy, 0f));
            _verts.Add(new Vector3(cx + hx, cy + hy, 0f));
            _verts.Add(new Vector3(cx - hx, cy + hy, 0f));
            _tris.Add(b); _tris.Add(b + 2); _tris.Add(b + 1);
            _tris.Add(b); _tris.Add(b + 3); _tris.Add(b + 2);
        }
        _mesh.Clear();
        _mesh.indexFormat = _verts.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        _mesh.SetVertices(_verts);
        _mesh.SetTriangles(_tris, 0);
        _mesh.RecalculateBounds();

        // ---- Colliders: many BoxCollider2D on one static object ----
        _colliderGO = new GameObject("WallColliders");
        _colliderGO.transform.SetParent(transform, false);
        foreach (var seg in maze.walls)
        {
            var col = _colliderGO.AddComponent<BoxCollider2D>();
            col.offset = seg.center;
            col.size = seg.size;
        }
    }
}
