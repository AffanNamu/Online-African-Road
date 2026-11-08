using System;

namespace ARO.NetCore
{
    /// <summary>
    /// Where a prop shows the text of its placement (E2, RB-07). A prop declares a `label` face in the catalogue; the four placeholder props that had their faces hard-coded in the chunk streamer
    /// (road sign, billboard, fuel station, bus stop) are kept as built-in defaults, with exactly the numbers they had, so existing routes read the same. Pure: the Unity layer draws the text.
    /// </summary>
    public static class PropLabelLayout
    {
        public struct Face { public V3 Local; public float Width, Height, YawDeg; public string Colour; public int FontMax; }

        public static bool Resolve(PropDef d, out Face face)
        {
            face = default;
            if (d == null) return false;
            if (d.HasLabel)
            {
                var l = d.label;
                face = new Face { Local = new V3(l.x, l.y, l.z), Width = l.width, Height = l.height, YawDeg = l.yawDeg, Colour = l.colour ?? "white", FontMax = l.fontMax };
                return true;
            }
            switch (d.id)
            {
                case "road_sign": face = Legacy(new V3(0f, d.heightM - 0.6f, 0.08f + d.depthM * 0.5f + 0.02f), d.widthM * 0.92f, 1.25f); return true;
                case "billboard": face = Legacy(new V3(0f, d.heightM - 2.1f, d.depthM * 0.5f + 0.03f), d.widthM * 0.9f, 3.6f); return true;
                case "fuel_station": face = Legacy(new V3(d.widthM * 0.1f, 5.5f, d.depthM * 0.375f + 0.03f), d.widthM * 0.55f, 0.5f); return true;
                case "bus_stop": face = Legacy(new V3(0f, d.heightM + 0.08f, d.depthM * 0.5f + 0.02f), d.widthM, 0.14f); return true;
            }
            return false;
        }

        static Face Legacy(V3 local, float w, float h) => new Face { Local = local, Width = w, Height = h, YawDeg = 0f, Colour = "white", FontMax = 0 };
    }
}
