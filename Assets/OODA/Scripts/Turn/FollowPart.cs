using UnityEngine;

namespace Ooda.Turn
{
    /// <summary>Keeps a red-hot copy of a part where the part is (a propeller turning, a control surface moving).</summary>
    public class FollowPart : MonoBehaviour
    {
        public Transform part;
        void LateUpdate()
        {
            if (part == null) return;
            transform.SetPositionAndRotation(part.position, part.rotation);
            transform.localScale = Vector3.one;
            var ls = part.lossyScale * 1.01f;
            var parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            transform.localScale = new Vector3(ls.x / Mathf.Max(1e-6f, parentScale.x), ls.y / Mathf.Max(1e-6f, parentScale.y), ls.z / Mathf.Max(1e-6f, parentScale.z));
        }
    }
}
