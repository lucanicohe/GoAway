using UnityEngine;

namespace FPCam
{
    /// <summary>
    /// Camera shake through Cinemachine Impulse. Lives next to a CinemachineImpulseSource (the setup adds both) and
    /// a CinemachineImpulseListener on the Cinemachine camera.
    /// From your own code:  FPCam.FirstPersonShake.Instance.Shake(1.5f);   (explosion = bigger number)
    /// (Uses reflection to call the Impulse Source, so this script compiles with or without Cinemachine installed.)
    /// </summary>
    [DisallowMultipleComponent]
    public class FirstPersonShake : MonoBehaviour
    {
        public static FirstPersonShake Instance { get; private set; }

        [Tooltip("The CinemachineImpulseSource component (found automatically if on the same object).")]
        public Component source;

        System.Reflection.MethodInfo generateWithForce, generate;

        void Awake()
        {
            Instance = this;
            if (source == null)
            {
                foreach (Component c in GetComponents<Component>())
                    if (c != null && c.GetType().Name == "CinemachineImpulseSource") { source = c; break; }
            }
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public void Shake(float force = 1f)
        {
            if (source == null) return;
            System.Type t = source.GetType();
            if (generateWithForce == null) generateWithForce = t.GetMethod("GenerateImpulse", new System.Type[] { typeof(float) });
            if (generateWithForce != null) { generateWithForce.Invoke(source, new object[] { force }); return; }
            if (generate == null) generate = t.GetMethod("GenerateImpulse", System.Type.EmptyTypes);
            if (generate != null) generate.Invoke(source, null);
        }

        [ContextMenu("Test Shake")]
        void TestShake() { Shake(1f); }
    }
}
