using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace QuadTree
{
    public class QuadTree<T> where T : WorldObject
    {
        internal enum Quadrants : int
        {
            None = -1,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight,
        }

        private readonly int maxObjects; // Maximum number of objects per node
        private readonly int maxLevels; // Maximum depth of the tree
        private readonly float leafNodeMultiplier;

        private readonly IObjectPool<Node> nodePool;
        private Node rootNode;
        private readonly Dictionary<T, Node> nodeDict;

#if DEBUG
        public int NodeCount => rootNode.NodeCount + 1;
        public int ObjectCount => rootNode.ObjectCount;
#endif

        public QuadTree(Rect initialBounds, int capacity, int maxObjectsPerNode, int maxTreeLevels, IObjectPool<Node> objectsPool = null)
        {
            maxObjects = maxObjectsPerNode;
            maxLevels = maxTreeLevels;
            leafNodeMultiplier = (float)System.Math.Pow(2, maxLevels); // Pow() is expensive, so only do it once
            nodePool = objectsPool;
            nodeDict = new Dictionary<T, Node>(capacity / 4);
            rootNode = GetNewNode();
            rootNode.Init(this, 0, initialBounds);
        }

        ~QuadTree()
        {
            Clear();
        }

        public void Insert(T obj)
        {
            while (!rootNode.InsertObject(obj))
            {
                Quadrants direction = rootNode.GetQuadrantByPosition(obj.Position);
                ExpandTreeOneLevel(direction);
            }
        }

        public bool Remove(T obj)
        {
            if (nodeDict.TryGetValue(obj, out Node node))
            {
                return node.RemoveObject(obj);
            }
            return false;
        }

        public void Clear() => rootNode.Clear();

        public void Update(T obj, Vector2 displacement)
        {
            if (displacement == Vector2.zero)
            {
                return;
            }

            // Calculate the size of the leaf nodes based on the maximum tree depth
            float leafNodeSize = rootNode.BoundingBox.size.x / leafNodeMultiplier;

            // Calculate the old positions within the grid
            float newMinX = obj.BoundingBox.min.x;
            float newMinY = obj.BoundingBox.min.y;
            float newMaxX = obj.BoundingBox.max.x;
            float newMaxY = obj.BoundingBox.max.y;
            float oldMinX = obj.BoundingBox.min.x - displacement.x;
            float oldMinY = obj.BoundingBox.min.y - displacement.y;
            float oldMaxX = obj.BoundingBox.max.x - displacement.x;
            float oldMaxY = obj.BoundingBox.max.y - displacement.y;

            // Calculate the grid indices for the old and current positions
            int oldGridMinIndexX = (int)System.Math.Floor(oldMinX / leafNodeSize);
            int oldGridMinIndexY = (int)System.Math.Floor(oldMinY / leafNodeSize);
            int oldGridMaxIndexX = (int)System.Math.Floor(oldMaxX / leafNodeSize);
            int oldGridMaxIndexY = (int)System.Math.Floor(oldMaxY / leafNodeSize);
            int newGridMinIndexX = (int)System.Math.Floor(newMinX / leafNodeSize);
            int newGridMinIndexY = (int)System.Math.Floor(newMinY / leafNodeSize);
            int newGridMaxIndexX = (int)System.Math.Floor(newMaxX / leafNodeSize);
            int newGridMaxIndexY = (int)System.Math.Floor(newMaxY / leafNodeSize);

            // Check if the object has not crossed grid boundaries
            if (oldGridMinIndexX == newGridMinIndexX &&
                oldGridMinIndexY == newGridMinIndexY &&
                oldGridMaxIndexX == newGridMaxIndexX &&
                oldGridMaxIndexY == newGridMaxIndexY)
            {
                return; // Object has not crossed grid boundaries
            }

            // Reinsert
            Rect oldPosBounds = obj.BoundingBox;
            oldPosBounds.center -= displacement;
            if (!rootNode.UpdateObject(obj, oldPosBounds))
            {
                Insert(obj);
            }
        }

        public void Query(ref Rect range, List<T> resultList) => rootNode.Query(range, resultList);

        private void ExpandTreeOneLevel(Quadrants outsideObjDirection)
        {
            Quadrants thisNodeQuadrant;
            Rect parentBounds = rootNode.BoundingBox;
            float minX = rootNode.BoundingBox.min.x;
            float minY = rootNode.BoundingBox.min.y;
            float maxX = rootNode.BoundingBox.max.x;
            float maxY = rootNode.BoundingBox.max.y;
            float sizeX = rootNode.BoundingBox.size.x;
            float sizeZ = rootNode.BoundingBox.size.y;

            switch (outsideObjDirection)
            {
                case Quadrants.TopLeft:
                    thisNodeQuadrant = Quadrants.BottomRight;
                    parentBounds.min = new Vector2(minX - sizeX, minY);
                    parentBounds.max = new Vector2(maxX, maxY + sizeZ);
                    break;
                case Quadrants.TopRight:
                    thisNodeQuadrant = Quadrants.BottomLeft;
                    parentBounds.min = new Vector2(minX, minY);
                    parentBounds.max = new Vector2(maxX + sizeX, maxY + sizeZ);
                    break;
                case Quadrants.BottomLeft:
                    thisNodeQuadrant = Quadrants.TopRight;
                    parentBounds.min = new Vector2(minX - sizeX, minY - sizeZ);
                    parentBounds.max = new Vector2(maxX, maxY);
                    break;
                case Quadrants.BottomRight:
                    thisNodeQuadrant = Quadrants.TopLeft;
                    parentBounds.min = new Vector2(minX, minY - sizeZ);
                    parentBounds.max = new Vector2(maxX + sizeX, maxY);
                    break;
                default:
                    throw new System.InvalidOperationException();
            }

            Node newRoot = GetNewNode();
            newRoot.Init(this, 0, parentBounds);
            newRoot.CreateChildNodes(new System.Tuple<Quadrants, Node>(thisNodeQuadrant, rootNode));
            rootNode = newRoot;
        }

        private Node GetNewNode()
        {
            if (nodePool != null)
            {
                Node newNode = nodePool.Get();
                return newNode ?? throw new System.OutOfMemoryException("The object pool has reached its capacity; unable to allocate more objects.");
            }
            return new Node();
        }

        private void DiscardNode(Node unusedNode) => nodePool?.Release(unusedNode);

        public class Node
        {
            private int level;
            private readonly List<T> objects; // Using 4 times the size for worst case (collapse of filled lowest level)
            private readonly Node[] nodes;
            private Rect bounds;
            private QuadTree<T> host;

            internal Rect BoundingBox => bounds;

#if DEBUG
            public int NodeCount
            {
                get
                {
                    int count = 0;
                    if (nodes[0] != null)
                    {
                        count += 4;
                        for (int i = 0; i < 4; i++)
                        {
                            count += nodes[i].NodeCount;
                        }
                    }
                    return count;
                }
            }

            public int ObjectCount
            {
                get
                {
                    int count = objects.Count;
                    if (nodes[0] != null)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            count += nodes[i].ObjectCount;
                        }
                    }
                    return count;
                }
            }
#endif

            public Node()
            {
                level = -1;
                bounds = new Rect(Vector2.one * float.NaN, Vector2.one * float.NaN);
                objects = new List<T>();
                nodes = new Node[4];
            }

            public void Init(QuadTree<T> holdingHost, int currentLevel, Rect nodeBounds)
            {
                host = holdingHost;
                level = currentLevel;
                bounds = nodeBounds;
            }

            public bool InsertObject(T obj)
            {
                if (!bounds.Overlaps(obj.BoundingBox))
                {
                    return false; // The object is not within this node's bounds
                }

                if (nodes[0] != null)
                {
                    // Insert the object into the appropriate child node
                    Quadrants quadrant = GetQuadrantByBoundingBox(obj);
                    if (quadrant != Quadrants.None)
                    {
                        nodes[(int)quadrant].InsertObject(obj);
                        return true;
                    }
                }

                // If the object doesn't fit into any child node, store it in this node
                objects.Add(obj);
                if (host.nodeDict.ContainsKey(obj))
                {
                    host.nodeDict[obj] = this;
                }
                else
                {
                    host.nodeDict.Add(obj, this);
                }

                // Check if the node should split due to exceeding the maximum objects
                if (objects.Count > host.maxObjects && level < host.maxLevels)
                {
                    Subdivide();
                }
                return true;
            }

            private void Subdivide()
            {
                if (nodes[0] == null)
                {
                    // Split the node into four child nodes if it's not already split
                    CreateChildNodes();
                }

                for (int i = objects.Count - 1; i >= 0; i--)
                {
                    // Insert objects that belong to child nodes into those child nodes
                    T obj = objects[i];
                    Quadrants quadrant = GetQuadrantByBoundingBox(obj);
                    if (quadrant != Quadrants.None)
                    {
                        host.nodeDict.Remove(obj);
                        objects.RemoveAt(i);
                        nodes[(int)quadrant].InsertObject(obj);
                    }
                }
            }

            public bool RemoveObject(T obj)
            {
                bool removed = objects.Remove(obj); // Try to remove the object from this node
                if (removed)
                {
                    host.nodeDict.Remove(obj);
                }

                if (nodes[0] != null)
                {
                    if (!removed)
                    {
                        // If there are child nodes, attempt to remove the object from them
                        for (int i = 0; i < 4; i++)
                        {
                            if (nodes[i].RemoveObject(obj))
                            {
                                removed = true; // The object was removed from a child node
                                break;
                            }
                        }
                    }

                    // After attempting to remove from child nodes, check if child nodes are all empty
                    bool allChildNodesEmpty = true;
                    for (int i = 0; i < 4; i++)
                    {
                        if (nodes[i].objects.Count > 0 || nodes[i].nodes[0] != null)
                        {
                            allChildNodesEmpty = false;
                            break;
                        }
                    }
                    if (allChildNodesEmpty)
                    {
                        // All child nodes are empty, remove them
                        Clear();
                    }
                }
                return removed;
            }

            public bool UpdateObject(T obj, Rect oldPosBounds)
            {
                bool usedToContain = bounds.Overlaps(oldPosBounds);
                bool shouldContain = bounds.Overlaps(obj.BoundingBox);

                if (!usedToContain && !shouldContain)
                {
                    return false; // The object never was within this node
                }
                if (usedToContain && shouldContain)
                {
                    if (objects.Contains(obj))
                    {
                        // Object has not left, nothing to do here
                        return true;
                    }
                    else
                    {
                        if (nodes[0] != null)
                        {
                            // Object is somewhere in the child nodes, search them
                            bool success = false;
                            for (int i = 0; i < 4; i++)
                            {
                                success = nodes[i].UpdateObject(obj, oldPosBounds) || success;
                            }
                            if (success)
                            {
                                return true;
                            }
                        }
                        return false;
                    }
                }
                else
                {
                    // Object has moved nodes, handle accordingly
                    RemoveObject(obj);
                    return InsertObject(obj);
                }
            }

            public void Clear()
            {
                objects.Clear();

                if (nodes[0] != null)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        nodes[i].Clear();
                        host.DiscardNode(nodes[i]);
                        nodes[i] = null;
                    }
                }
            }

            public void Query(Rect range, List<T> result)
            {
                if (!bounds.Overlaps(range))
                {
                    return; // No intersection
                }

                // Check objects in this node
                for (int i = 0; i < objects.Count; i++)
                {
                    T obj = objects[i];
                    if (obj.BoundingBox.Overlaps(range))
                    {
                        result.Add(obj); // Add the object to the result if it intersects with the query bounding range
                    }
                }

                // If there are child nodes, recursively query them
                if (nodes[0] != null)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        nodes[i].Query(range, result);
                    }
                }
            }

            internal void CreateChildNodes(System.Tuple<Quadrants, Node> existingSubNode = null)
            {
                Rect boundingBox = bounds;
                float minX = bounds.min.x;
                float minY = bounds.min.y;
                float maxX = bounds.max.x;
                float maxY = bounds.max.y;
                float centerX = bounds.center.x;
                float centerZ = bounds.center.y;

                // Check if an existing subnode is provided
                if (existingSubNode != null)
                {
                    int index = (int)existingSubNode.Item1;
                    nodes[index] = existingSubNode.Item2;
                    nodes[index].host = host;
                    nodes[index].IncreaseLevel();
                }

                // Create new objects for the children that are still null
                if (nodes[0] == null)
                {
                    boundingBox.min = new Vector2(minX, centerZ);
                    boundingBox.max = new Vector2(centerX, maxY);
                    // Create top-left node
                    nodes[0] = host.GetNewNode();
                    nodes[0].Init(host, level + 1, boundingBox);
                }
                if (nodes[1] == null)
                {
                    boundingBox.min = new Vector2(centerX, centerZ);
                    boundingBox.max = new Vector2(maxX, maxY);
                    // Create top-right node
                    nodes[1] = host.GetNewNode();
                    nodes[1].Init(host, level + 1, boundingBox);
                }
                if (nodes[2] == null)
                {
                    boundingBox.min = new Vector2(minX, minY);
                    boundingBox.max = new Vector2(centerX, centerZ);
                    // Create bottom-left node
                    nodes[2] = host.GetNewNode();
                    nodes[2].Init(host, level + 1, boundingBox);
                }
                if (nodes[3] == null)
                {
                    boundingBox.min = new Vector2(centerX, minY);
                    boundingBox.max = new Vector2(maxX, centerZ);
                    // Create bottom-right node
                    nodes[3] = host.GetNewNode();
                    nodes[3].Init(host, level + 1, boundingBox);
                }
            }

            private void IncreaseLevel()
            {
                level++;

                if (nodes[0] != null)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        nodes[i].IncreaseLevel();
                    }
                }

                if (level >= host.maxLevels)
                {
                    if (nodes[0] != null)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            objects.AddRange(nodes[i].objects);
                            for (int j = 0; j < nodes[i].objects.Count; j++)
                            {
                                host.nodeDict[nodes[i].objects[j]] = this;
                            }
                            nodes[i].Clear();
                        }
                    }
                }
            }

            internal Quadrants GetQuadrantByPosition(Vector3 position)
            {
                Vector3 nodeCenter = bounds.center;

                if (position.x <= nodeCenter.x)
                {
                    // Object is in the left half
                    if (position.y >= nodeCenter.y)
                    {
                        return Quadrants.TopLeft;
                    }
                    else
                    {
                        return Quadrants.BottomLeft;
                    }
                }
                else
                {
                    // Object is in the right half
                    if (position.y >= nodeCenter.y)
                    {
                        return Quadrants.TopRight;
                    }
                    else
                    {
                        return Quadrants.BottomRight;
                    }
                }
            }

            private Quadrants GetQuadrantByBoundingBox(T obj)
            {
                if (!bounds.Contains(obj.BoundingBox.min) || !bounds.Contains(obj.BoundingBox.max))
                {
                    return Quadrants.None;
                }

                Vector3 objMin = obj.BoundingBox.min;
                Vector3 objMax = obj.BoundingBox.max;
                Vector3 nodeCenter = bounds.center;

                if (objMax.x < nodeCenter.x)
                {
                    // Object is in the left half
                    if (objMin.y > nodeCenter.y)
                    {
                        return Quadrants.TopLeft;
                    }
                    else if (objMax.y < nodeCenter.y)
                    {
                        return Quadrants.BottomLeft;
                    }
                }
                else if (objMin.x > nodeCenter.x)
                {
                    // Object is in the right half
                    if (objMin.y > nodeCenter.y)
                    {
                        return Quadrants.TopRight;
                    }
                    else if (objMax.y < nodeCenter.y)
                    {
                        return Quadrants.BottomRight;
                    }
                }
                // Object lies outside of node or on the border between two quadrants
                return Quadrants.None;
            }

#if UNITY_EDITOR
            public void DebugDrawNodeRecursive(Vector3 offset, Color color)
            {
                UnityEditor.Handles.color = color;
                UnityEditor.Handles.DrawWireCube(offset + new Vector3(bounds.center.x, 0f, bounds.center.y), new Vector3(bounds.size.x, 0f, bounds.size.y));

                if (nodes[0] != null)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        nodes[i].DebugDrawNodeRecursive(offset, color);
                    }
                }
            }
#endif
        }

#if UNITY_EDITOR
        public void DebugDraw(Vector3 offset, Color color)
        {
            rootNode.DebugDrawNodeRecursive(offset, color);
        }
#endif

    }
}