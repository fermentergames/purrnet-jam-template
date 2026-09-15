using UnityEngine;

namespace Jam
{
    /// <summary>
    /// Hides the main menu "Quit" button on WebGL builds, where quitting the
    /// application is not supported. Attach to the main menu root GameObject.
    /// </summary>
    public class HideQuitOnWebGL : MonoBehaviour
    {
        [Tooltip("Optional direct reference to the Quit button. If unset, the button is found by name.")]
        [SerializeField] private GameObject _quitButton;

        private void Awake()
        {
            if (Application.platform != RuntimePlatform.WebGLPlayer)
                return;

            if (_quitButton)
            {
                _quitButton.SetActive(false);
                return;
            }

            var quit = FindChildByName(transform, "Quit");
            if (quit)
                quit.gameObject.SetActive(false);
        }

        private static Transform FindChildByName(Transform parent, string name)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name)
                    return child;

                var nested = FindChildByName(child, name);
                if (nested)
                    return nested;
            }

            return null;
        }
    }
}