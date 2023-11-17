using QuadTree;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

public class ExampleQuadTreeHolder : MonoBehaviour
{
    public static ExampleQuadTreeHolder Instance; // wanna-be Singleton

    public int objectCapacity = 100000;
    public int maxObjectsPerNode = 10;
    public int maxTreeLevels = 10;

    [Space]

    public Vector2 size = Vector2.one * 100f;

    public QuadTree<WorldObject> quadTree;

    void Awake()
    {
        Rect boundingBox = new Rect(Vector2.zero, size);
        boundingBox.center = transform.position;
        quadTree = new QuadTree<WorldObject>(boundingBox, objectCapacity, maxObjectsPerNode, maxTreeLevels, null);
        Instance = this;
    }

    public int OBJS;
    public int NODES;

    private void Update()
    {
        OBJS = quadTree.ObjectCount;
        NODES = quadTree.NodeCount;
    }

    void OnDrawGizmos()
    {
#if UNITY_EDITOR
        Vector3 pos = transform.position;
        pos.y = 0f;
        if (quadTree != null)
        {
            quadTree.DebugDraw(pos, Color.red);
        }
        else
        {
            UnityEditor.Handles.color = Color.red;
            UnityEditor.Handles.DrawWireCube(transform.position, new Vector3(size.x, 0f, size.y));
        }
#endif
    }
}
