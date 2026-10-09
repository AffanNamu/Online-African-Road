using System;
using UnityEngine;

namespace ARO.World
{
    public enum ZoneType { Urban, Industrial, Commercial, Rural, Highway }
    public enum RoadSurface { Good, Worn, Damaged }

    [Serializable]
    public struct RouteNode
    {
        public Vector3 position;
        public float width;         // metres, full carriageway
        public ZoneType zone;       // drives roadside content
        public RoadSurface surface; // surface of the segment that STARTS at this node
    }

    /// <summary>
    /// A drivable corridor as data (no hard-coded Lagos route in code). The chunk streamer builds
    /// road and roadside content from this; the server's `locations` rows reference the same coordinates.
    /// </summary>
    [CreateAssetMenu(menuName = "African Roads/Route Definition")]
    public class RouteDefinition : ScriptableObject
    {
        public string routeId = "ng-lagos-ibadan";
        public int seed = 1180;
        public RouteNode[] nodes = new RouteNode[0];

        public float LengthMetres()
        {
            float d = 0f;
            for (int i = 1; i < nodes.Length; i++) d += Vector3.Distance(nodes[i - 1].position, nodes[i].position);
            return d;
        }
    }
}
