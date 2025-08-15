using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace JanSharp.Internal
{
        public abstract class LockstepGameStateEntryBase : UdonSharpBehaviour
        {
#if !LOCKSTEP_DEBUG
                [HideInInspector]
#endif
                public TextMeshProUGUI displayNameText;
#if !LOCKSTEP_DEBUG
                [HideInInspector]
#endif
                public Toggle mainToggle;
#if !LOCKSTEP_DEBUG
                [HideInInspector]
#endif
                public Image toggledImage;
#if !LOCKSTEP_DEBUG
                [HideInInspector]
#endif
                public Color goodColor;
#if !LOCKSTEP_DEBUG
                [HideInInspector]
#endif
                public Color badColor;
        }
}
