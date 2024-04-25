using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using TMPro;

namespace JanSharp.Internal
{
    public abstract class LockstepGameStateEntryBase : UdonSharpBehaviour
    {
        #if !LockstepDebug
        [HideInInspector]
        #endif
        public TextMeshProUGUI displayNameText;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        public Toggle mainToggle;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        public Image toggledImage;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        public Color goodColor;
        #if !LockstepDebug
        [HideInInspector]
        #endif
        public Color badColor;
    }
}
