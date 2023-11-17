using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace QuadTree
{
    public class WorldObject : MonoBehaviour
    {
        public Vector2 size = Vector2.one;
        public Vector3 Position => transform.position;
        public Rect BoundingBox => new Rect(new Vector2(Position.x, Position.z), size);

        private Vector3 lastPos;

        protected void Awake()
        {
            lastPos = Position;
        }

        protected void OnEnable()
        {
            ExampleQuadTreeHolder.Instance.quadTree.Insert(this);
        }

        protected void OnDisable()
        {
            ExampleQuadTreeHolder.Instance.quadTree.Remove(this);
        }

        protected void LateUpdate()
        {
            Vector3 displacement = Position - lastPos;
            ExampleQuadTreeHolder.Instance.quadTree.Update(this, new Vector2(displacement.x, displacement.z));
            lastPos = Position;
        }

#if UNITY_EDITOR
        protected void OnDrawGizmosSelected()
        {
            UnityEditor.Handles.color = Color.cyan;
            UnityEditor.Handles.DrawWireCube(transform.position, new Vector3(size.x, 0f, size.y));
        }
#endif
    }
}
