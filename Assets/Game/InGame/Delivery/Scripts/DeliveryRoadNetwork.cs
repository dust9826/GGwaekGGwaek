using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    public sealed class DeliveryRoadNetwork : MonoBehaviour
    {
        [SerializeField] private DeliveryRoadNode[] _nodes = Array.Empty<DeliveryRoadNode>();
        [SerializeField] private DeliveryRoadSegment[] _segments = Array.Empty<DeliveryRoadSegment>();
        [SerializeField] private DeliveryFactory[] _factories = Array.Empty<DeliveryFactory>();

        public IReadOnlyList<DeliveryRoadNode> Nodes => _nodes;
        public IReadOnlyList<DeliveryRoadSegment> Segments => _segments;
        public IReadOnlyList<DeliveryFactory> Factories => _factories;

        public void Configure(IReadOnlyList<DeliveryRoadNode> nodes,
                              IReadOnlyList<DeliveryRoadSegment> segments,
                              IReadOnlyList<DeliveryFactory> factories)
        {
            _nodes = Copy(nodes);
            _segments = Copy(segments);
            _factories = Copy(factories);
        }

        public DeliveryRoadNode FindNearestNode(Vector3 worldPosition)
        {
            DeliveryRoadNode nearest = null;
            float bestSqrDistance = float.PositiveInfinity;
            foreach (DeliveryRoadNode node in _nodes)
            {
                if (node == null) continue;
                float sqrDistance = (node.transform.position - worldPosition).sqrMagnitude;
                if (sqrDistance >= bestSqrDistance) continue;
                bestSqrDistance = sqrDistance;
                nearest = node;
            }
            return nearest;
        }

        public bool TryValidate(out string error)
        {
            if (_nodes == null || _nodes.Length == 0)
            {
                error = "배송 도로 노드가 없다";
                return false;
            }
            if (_segments == null || _segments.Length == 0)
            {
                error = "배송 도로 구간이 없다";
                return false;
            }
            if (_factories == null || _factories.Length < 2)
            {
                error = "배송 공장은 최소 두 곳이 필요하다";
                return false;
            }

            var nodeSet = new HashSet<DeliveryRoadNode>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (DeliveryRoadNode node in _nodes)
            {
                if (node == null)
                {
                    error = "비어 있는 배송 도로 노드가 있다";
                    return false;
                }
                if (!nodeSet.Add(node))
                {
                    error = $"중복 도로 노드: {node.name}";
                    return false;
                }
                if (!ids.Add(node.Id))
                {
                    error = $"중복 도로 노드 ID: {node.Id}";
                    return false;
                }
            }

            foreach (DeliveryRoadSegment segment in _segments)
            {
                if (segment == null)
                {
                    error = "비어 있는 배송 도로 구간이 있다";
                    return false;
                }
                if (!segment.TryValidate(out error)) return false;
                if (!nodeSet.Contains(segment.Start) || !nodeSet.Contains(segment.End))
                {
                    error = $"{segment.name}: 네트워크에 등록되지 않은 노드를 참조한다";
                    return false;
                }
            }

            foreach (DeliveryFactory factory in _factories)
            {
                if (factory == null || factory.RoadNode == null)
                {
                    error = "도로 노드가 없는 배송 공장이 있다";
                    return false;
                }
                if (!nodeSet.Contains(factory.RoadNode))
                {
                    error = $"{factory.name}: 네트워크에 등록되지 않은 노드를 참조한다";
                    return false;
                }
            }

            DeliveryRoadNode origin = _factories[0].RoadNode;
            foreach (DeliveryFactory factory in _factories)
            {
                if (factory.RoadNode == origin) continue;
                if (!DeliveryRoutePlanner.TryPlan(this, origin, factory.RoadNode, out _))
                {
                    error = $"{_factories[0].name}에서 {factory.name}까지 갈 수 없다";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private static T[] Copy<T>(IReadOnlyList<T> source)
        {
            if (source == null) return Array.Empty<T>();
            var result = new T[source.Count];
            for (int index = 0; index < source.Count; index++) result[index] = source[index];
            return result;
        }
    }
}
