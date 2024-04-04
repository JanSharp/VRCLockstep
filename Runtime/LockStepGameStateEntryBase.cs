using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
using TMPro;

namespace JanSharp
{
    public abstract class LockStepGameStateEntryBase : UdonSharpBehaviour
    {
        public TextMeshProUGUI displayNameText;
    }
}
