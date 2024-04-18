using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using TMPro;

namespace JanSharp
{
    public abstract class LockstepGameStateEntryBase : UdonSharpBehaviour
    {
        public TextMeshProUGUI displayNameText;
        public Toggle mainToggle;
        public Image toggledImage;
        public Color goodColor;
        public Color badColor;
    }
}
