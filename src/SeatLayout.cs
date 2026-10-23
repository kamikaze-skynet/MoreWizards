using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoreWizards;

/// <summary>
/// Where to put lobby seats 5+.
///  Ring      - in the gaps between the existing chairs around the table, nudged outward.
///              If the chairs only cover part of the circle (an arc facing the camera), the open side is left empty.
///  SecondRow - behind the existing chairs (away from the table), staggered half a seat sideways.
///  Line      - continue the row past the last chair (1.0-1.2 behaviour).
///  Auto      - Ring, unless the original chairs are in a straight line, then SecondRow.
/// </summary>
internal static class SeatLayout
{
    internal struct Placement
    {
        public int Source;          // original seat this one is copied from
        public Vector3 Position;    // world position of the new seat
        public Quaternion Turn;     // yaw applied relative to the source seat
    }

    internal static readonly List<Placement> Placements = new();
    internal static string UsedLayout = "none";
    private static Transform[] originals;

    internal static void Plan(Transform[] seats, int extra)
    {
        Placements.Clear();
        originals = seats;
        int n = seats.Length;
        if (n == 0 || extra <= 0) return;

        string mode = (Plugin.SeatLayoutMode.Value ?? "Auto").Trim();
        float outward = Plugin.ExtraSeatOutwardOffset.Value;

        // Geometry of the original chairs (flattened to the floor plane).
        Vector3 center = Vector3.zero;
        foreach (var s in seats) center += s.position;
        center /= n;
        float spacing = AverageNeighbourSpacing(seats);
        bool straight = IsRoughlyStraight(seats, spacing);

        if (mode.Equals("Auto", StringComparison.OrdinalIgnoreCase)) mode = (n >= 3 && !straight) ? "Ring" : "SecondRow";

        if (mode.Equals("Ring", StringComparison.OrdinalIgnoreCase) && n >= 3) PlanRing(seats, center, extra, outward);
        else if (mode.Equals("Line", StringComparison.OrdinalIgnoreCase)) PlanLine(seats, extra);
        else PlanSecondRow(seats, extra, spacing, outward);
    }

    private static void PlanLine(Transform[] seats, int extra)
    {
        UsedLayout = "Line";
        int n = seats.Length;
        Vector3 step = n >= 2 ? seats[n - 1].position - seats[n - 2].position : seats[0].right * 1.5f;
        for (int k = 0; k < extra; k++)
            Placements.Add(new Placement { Source = n - 1, Position = seats[n - 1].position + step * (k + 1), Turn = Quaternion.identity });
    }

    private static void PlanSecondRow(Transform[] seats, int extra, float spacing, float outward)
    {
        UsedLayout = "SecondRow";
        int n = seats.Length;
        float rowDepth = Mathf.Max(0.8f, spacing * 0.9f) + outward;
        for (int k = 0; k < extra; k++)
        {
            int src = k % n;
            int row = 1 + k / n;
            Transform s = seats[src];
            Vector3 back = Flat(-s.forward);
            Vector3 side = Flat(s.right);
            Vector3 pos = s.position + back * (rowDepth * row) + side * (spacing * 0.5f * (row % 2 == 1 ? 1f : -1f));
            Placements.Add(new Placement { Source = src, Position = pos, Turn = Quaternion.identity });
        }
    }

    private static void PlanRing(Transform[] seats, Vector3 center, int extra, float outward)
    {
        UsedLayout = "Ring";
        int n = seats.Length;

        // Angles of the chairs around the table centre, sorted.
        var order = new List<(float angle, int index)>();
        float radius = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 d = Flat3(seats[i].position - center);
            order.Add((Mathf.Atan2(d.z, d.x), i));
            radius += d.magnitude;
        }
        radius = radius / n + outward;
        order.Sort((a, b) => a.angle.CompareTo(b.angle));

        // Gaps between neighbouring chairs. A gap of ~180 deg or more is the open side (e.g. facing the camera): skip it.
        var gaps = new List<(float start, float size, int src, int count)>();
        for (int i = 0; i < n; i++)
        {
            var a = order[i];
            var b = order[(i + 1) % n];
            float size = b.angle - a.angle;
            if (size <= 0f) size += Mathf.PI * 2f;
            gaps.Add((a.angle, size, a.index, 0));
        }
        gaps.Sort((x, y) => y.size.CompareTo(x.size));
        if (gaps.Count > 1 && gaps[0].size >= Mathf.PI * 0.95f) gaps.RemoveAt(0);

        // Hand out seats so each gap's chairs are evenly spaced.
        for (int k = 0; k < extra; k++)
        {
            int best = 0;
            for (int g = 1; g < gaps.Count; g++)
                if (gaps[g].size / (gaps[g].count + 1) > gaps[best].size / (gaps[best].count + 1)) best = g;
            var t = gaps[best];
            gaps[best] = (t.start, t.size, t.src, t.count + 1);
        }

        foreach (var g in gaps)
        {
            for (int j = 1; j <= g.count; j++)
            {
                float angle = g.start + g.size * j / (g.count + 1);
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Transform src = seats[g.src];
                Vector3 srcDir = Flat(src.position - center);
                Vector3 pos = center + dir * radius;
                pos.y = src.position.y;
                Placements.Add(new Placement { Source = g.src, Position = pos, Turn = Quaternion.FromToRotation(srcDir, dir) });
            }
        }
    }

    /// <summary>Move a copied seat (or anything attached to a seat) by the placement's transform.</summary>
    internal static void Place(Transform target, Transform sourceSeat, Placement p)
    {
        target.position = p.Position;
        target.rotation = p.Turn * sourceSeat.rotation;
    }

    /// <summary>Seat cameras 5+ get the same move as their seat, relative to the source seat's camera.</summary>
    internal static void PlaceCameras(MainMenuCameraController controller)
    {
        if (originals == null || Placements.Count == 0) return;
        var cams = controller.seatCameras;
        if (cams == null) return;
        int n = originals.Length;
        for (int k = 0; k < Placements.Count; k++)
        {
            int idx = n + k;
            if (idx >= cams.Length || cams[idx] == null) break;
            var p = Placements[k];
            if (p.Source >= cams.Length || cams[p.Source] == null || originals[p.Source] == null) continue;

            Transform srcCam = cams[p.Source].transform;
            Transform srcSeat = originals[p.Source];
            Transform cam = cams[idx].transform;
            cam.position = p.Position + p.Turn * (srcCam.position - srcSeat.position);
            cam.rotation = p.Turn * srcCam.rotation;
        }
    }

    // ---------- helpers ----------

    private static Vector3 Flat3(Vector3 v) { v.y = 0f; return v; }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
    }

    private static float AverageNeighbourSpacing(Transform[] seats)
    {
        if (seats.Length < 2) return 1.5f;
        float total = 0f;
        for (int i = 0; i < seats.Length; i++)
        {
            float best = float.MaxValue;
            for (int j = 0; j < seats.Length; j++)
                if (i != j) best = Mathf.Min(best, Flat3(seats[i].position - seats[j].position).magnitude);
            total += best;
        }
        return Mathf.Max(0.5f, total / seats.Length);
    }

    /// <summary>True if every chair is within a fraction of a seat-width of the line through the two farthest chairs.</summary>
    private static bool IsRoughlyStraight(Transform[] seats, float spacing)
    {
        if (seats.Length < 3) return true;
        int a = 0, b = 1; float far = -1f;
        for (int i = 0; i < seats.Length; i++)
            for (int j = i + 1; j < seats.Length; j++)
            {
                float d = Flat3(seats[i].position - seats[j].position).sqrMagnitude;
                if (d > far) { far = d; a = i; b = j; }
            }
        Vector3 p0 = Flat3(seats[a].position);
        Vector3 axis = Flat(seats[b].position - seats[a].position);
        foreach (var s in seats)
        {
            Vector3 v = Flat3(s.position) - p0;
            Vector3 off = v - axis * Vector3.Dot(v, axis);
            if (off.magnitude > spacing * 0.25f) return false;
        }
        return true;
    }
}
