using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cuts a bar off along a straight line at any angle (used by VisorHud, added to the bar's "Fill" automatically).
/// Nothing is scaled or squished: the sprite keeps its size and its stripes, the geometry is just clipped.
/// The line is placed so that the VISIBLE part of the bar equals the amount (50% = half of what you can see is left).
/// It measures the sprite's actual pixels, so empty margins or slanted ends of the sprite never make the line
/// "travel through nothing" while the bar looks like it is waiting before it starts to empty.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("")]
public class VisorHudBarWipe : BaseMeshEffect
{
    float amount = 1f;
    Vector2 dir = Vector2.right;          // direction the empty part travels, in the Fill's local space

    const int GridSize = 64;              // the sprite's alpha is sampled on a 64 x 64 grid
    const int Bins = 128;                 // resolution of the "how much is visible before this line" table
    static readonly float[] bins = new float[Bins];
    float[] alphaGrid;                    // row 0 = bottom of the sprite, null = could not be measured (rectangle is used instead)
    Sprite measuredSprite;
    bool measureDone;

    static readonly List<UIVertex> stream = new List<UIVertex>();
    static readonly List<UIVertex> clipped = new List<UIVertex>();
    static readonly UIVertex[] polyIn = new UIVertex[3];
    static readonly UIVertex[] polyOut = new UIVertex[4];

    /// <summary>amount 1 = full bar, 0 = empty. direction = the way the empty part grows (local space).</summary>
    public void Set(float newAmount, Vector2 direction)
    {
        Measure();
        newAmount = Mathf.Clamp01(newAmount);
        if (direction.sqrMagnitude < 0.000001f) direction = Vector2.right;
        direction.Normalize();
        if (Mathf.Abs(newAmount - amount) < 0.00001f && (direction - dir).sqrMagnitude < 0.0000001f) return;
        amount = newAmount;
        dir = direction;
        if (graphic != null) graphic.SetVerticesDirty();
    }

    // Reads the sprite's alpha once (GPU copy, so the texture does not need Read/Write enabled). Runs from Set(), never while the UI is rebuilding.
    void Measure()
    {
        Image img = graphic as Image;
        Sprite sp = img != null ? img.sprite : null;
        if (measureDone && sp == measuredSprite) return;
        measureDone = true;
        measuredSprite = sp;
        alphaGrid = null;
        if (sp == null || sp.texture == null) return;

        RenderTexture rt = null;
        RenderTexture previous = RenderTexture.active;
        Texture2D tmp = null;
        try
        {
            Texture tex = sp.texture;
            Rect r = sp.textureRect;
            rt = RenderTexture.GetTemporary(GridSize, GridSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(tex, rt, new Vector2(r.width / tex.width, r.height / tex.height), new Vector2(r.x / tex.width, r.y / tex.height));
            RenderTexture.active = rt;
            tmp = new Texture2D(GridSize, GridSize, TextureFormat.RGBA32, false);
            tmp.ReadPixels(new Rect(0, 0, GridSize, GridSize), 0, 0);
            tmp.Apply();
            Color32[] px = tmp.GetPixels32();
            float[] grid = new float[GridSize * GridSize];
            for (int i = 0; i < grid.Length; i++) grid[i] = px[i].a / 255f;
            alphaGrid = grid;
        }
        catch (System.Exception)
        {
            alphaGrid = null;                                   // fall back to the plain rectangle
        }
        finally
        {
            RenderTexture.active = previous;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            if (tmp != null) { if (Application.isPlaying) Destroy(tmp); else DestroyImmediate(tmp); }
        }
        if (graphic != null) graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || amount >= 0.9999f) return;          // full bar: leave the mesh alone
        if (amount <= 0.0001f) { vh.Clear(); return; }          // empty bar: nothing to draw

        vh.GetUIVertexStream(stream);
        int n = stream.Count;
        if (n < 3) return;

        // size of the bar and its length along the direction
        float min = float.MaxValue, max = float.MinValue;
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = stream[i].position;
            float d = p.x * dir.x + p.y * dir.y;
            if (d < min) min = d;
            if (d > max) max = d;
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }
        float edge = EdgeFromVisible(min, max, minX, maxX, minY, maxY, 1f - amount);   // everything before this line is empty

        clipped.Clear();
        for (int i = 0; i + 2 < n; i += 3)
            ClipTriangle(stream[i], stream[i + 1], stream[i + 2], edge);

        vh.Clear();
        vh.AddUIVertexTriangleStream(clipped);
    }

    // Position of the cutting line so that `empty` (0..1) of the VISIBLE pixels is cut off.
    float EdgeFromVisible(float min, float max, float minX, float maxX, float minY, float maxY, float empty)
    {
        float range = max - min;
        if (range <= 0.00001f) return min;

        if (alphaGrid != null)
        {
            for (int i = 0; i < Bins; i++) bins[i] = 0f;
            float total = 0f;
            for (int gy = 0; gy < GridSize; gy++)
            {
                float y = Mathf.Lerp(minY, maxY, (gy + 0.5f) / GridSize);
                for (int gx = 0; gx < GridSize; gx++)
                {
                    float a = alphaGrid[gy * GridSize + gx];
                    if (a < 0.02f) continue;
                    float x = Mathf.Lerp(minX, maxX, (gx + 0.5f) / GridSize);
                    int b = Mathf.Clamp((int)(((x * dir.x + y * dir.y) - min) / range * Bins), 0, Bins - 1);
                    bins[b] += a;
                    total += a;
                }
            }
            if (total > 0.0001f)
            {
                float target = empty * total, acc = 0f;
                for (int b = 0; b < Bins; b++)
                {
                    if (acc + bins[b] >= target)
                    {
                        float f = bins[b] > 0f ? (target - acc) / bins[b] : 0f;
                        return min + (b + f) / Bins * range;
                    }
                    acc += bins[b];
                }
                return max;
            }
        }

        // could not measure the sprite: use the whole rectangle
        float travel = EmptyDistance(Mathf.Abs(dir.x) * (maxX - minX), Mathf.Abs(dir.y) * (maxY - minY), empty);
        return Mathf.Min(min + travel, max);
    }

    // How far the cutting line has to travel from the first corner so that `empty` (0..1) of the bar's area is cut off.
    // a, b = how far the two sides of the bar stretch along the direction (|dir.x| * width and |dir.y| * height).
    static float EmptyDistance(float a, float b, float empty)
    {
        if (a < b) { float t = a; a = b; b = t; }              // a = the longer one
        if (a <= 0.0001f) return 0f;
        if (b <= 0.0001f) return a * empty;                    // line parallel to a side: plain linear
        float corner = b / (2f * a);                           // share of the area that is just the corner triangle
        if (empty <= corner) return Mathf.Sqrt(2f * a * b * empty);
        if (empty >= 1f - corner) return a + b - Mathf.Sqrt(2f * a * b * (1f - empty));
        return a * empty + b * 0.5f;
    }

    float Side(UIVertex v, float edge) { return v.position.x * dir.x + v.position.y * dir.y - edge; }

    void ClipTriangle(UIVertex a, UIVertex b, UIVertex c, float edge)
    {
        polyIn[0] = a; polyIn[1] = b; polyIn[2] = c;
        int count = 0;
        for (int i = 0; i < 3; i++)
        {
            UIVertex cur = polyIn[i];
            UIVertex nxt = polyIn[(i + 1) % 3];
            float dc = Side(cur, edge), dn = Side(nxt, edge);
            bool curIn = dc >= 0f, nxtIn = dn >= 0f;
            if (curIn) polyOut[count++] = cur;
            if (curIn != nxtIn) polyOut[count++] = Lerp(cur, nxt, dc / (dc - dn));
        }
        for (int i = 1; i + 1 < count; i++)                     // turn the clipped polygon back into triangles
        {
            clipped.Add(polyOut[0]);
            clipped.Add(polyOut[i]);
            clipped.Add(polyOut[i + 1]);
        }
    }

    static UIVertex Lerp(UIVertex a, UIVertex b, float k)
    {
        UIVertex v = a;
        v.position = Vector3.Lerp(a.position, b.position, k);
        v.color = Color32.Lerp(a.color, b.color, k);
        v.uv0 = Vector4.Lerp(a.uv0, b.uv0, k);
        v.uv1 = Vector4.Lerp(a.uv1, b.uv1, k);
        v.uv2 = Vector4.Lerp(a.uv2, b.uv2, k);
        v.uv3 = Vector4.Lerp(a.uv3, b.uv3, k);
        v.normal = Vector3.Lerp(a.normal, b.normal, k);
        v.tangent = Vector4.Lerp(a.tangent, b.tangent, k);
        return v;
    }
}