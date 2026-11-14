using UnityEngine;

namespace FPCam
{
    /// <summary>Hides and locks the mouse cursor while playing (press Esc in the editor to release it).</summary>
    public class CursorLockOnPlay : MonoBehaviour
    {
        void OnEnable()
        {
            if (!Application.isPlaying) return;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void OnDisable()
        {
            if (!Application.isPlaying) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
