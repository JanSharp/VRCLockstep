using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace JanSharp.Internal
{
    [InitializeOnLoad]
    public static class LockstepGameStateOptionsUIOnBuild
    {
        private static HashSet<System.Type> knownValidTypes = new();
        private const BindingFlags PrivateAndPublicFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        static LockstepGameStateOptionsUIOnBuild()
        {
            knownValidTypes.Clear();
            OnBuildUtil.RegisterTypeCumulative<LockstepGameStateOptionsUI>(OnBuildCumulative, order: -350000);
            foreach (System.Type ubType in OnAssemblyLoadUtil.AllUdonSharpBehaviourTypes)
                EarlyChecks(ubType);
        }

        private static bool OnBuildCumulative(IEnumerable<LockstepGameStateOptionsUI> optionsUIs)
        {
            bool result = true;
            foreach (LockstepGameStateOptionsUI optionsUI in optionsUIs)
                result &= OnBuild(optionsUI);
            return result;
        }

        private static bool OnBuild(LockstepGameStateOptionsUI optionsUI)
        {
            System.Type ubType = optionsUI.GetType();
            if (knownValidTypes.Contains(ubType))
                return true;

            string optionsClassName = optionsUI.OptionsClassName;
            if (string.IsNullOrWhiteSpace(optionsClassName))
            {
                Debug.LogError($"[Lockstep] The {ubType.Name} class, a {nameof(LockstepGameStateOptionsUI)}, "
                    + $"does not have a valid class name defined as its "
                    + $"{nameof(LockstepGameStateOptionsUI.OptionsClassName)}.", optionsUI);
                return false;
            }

            FieldInfo currentOptionsField = EditorUtil.GetFieldIncludingBase(ubType, "currentOptions", PrivateAndPublicFlags);
            FieldInfo optionsToValidateField = EditorUtil.GetFieldIncludingBase(ubType, "optionsToValidate", PrivateAndPublicFlags);
            if (currentOptionsField == null || optionsToValidateField == null)
            {
                Debug.LogError($"[Lockstep] The {ubType.Name} class, a {nameof(LockstepGameStateOptionsUI)}, "
                    + $"is missing the field 'currentOptions' or 'optionsToValidate' or both. "
                    + $"Said fields must be defined and both use the field type as described by "
                    + $"{nameof(LockstepGameStateOptionsUI.OptionsClassName)}. They can be private.", optionsUI);
                return false;
            }

            System.Type currentOptionsType = currentOptionsField.FieldType;
            System.Type optionsToValidateType = optionsToValidateField.FieldType;
            if (currentOptionsType != optionsToValidateType)
            {
                Debug.LogError($"[Lockstep] The {ubType.Name} class, a {nameof(LockstepGameStateOptionsUI)}, "
                    + $"has both the fields 'currentOptions' and 'optionsToValidate' defined, however "
                    + $"they don't both share the same field type. "
                    + $"'{currentOptionsType.Name}' vs '{optionsToValidateType.Name}'.", optionsUI);
                return false;
            }

            if (currentOptionsType.Name != optionsClassName)
            {
                Debug.LogError($"[Lockstep] The {ubType.Name} class, a {nameof(LockstepGameStateOptionsUI)}, "
                    + $"has both the fields 'currentOptions' and 'optionsToValidate' defined, however "
                    + $"their type does not match what was defined by "
                    + $"{nameof(LockstepGameStateOptionsUI.OptionsClassName)}. "
                    + $"'{currentOptionsType.Name}' vs '{optionsClassName}'.", optionsUI);
                return false;
            }

            if (!EditorUtil.DerivesFrom(currentOptionsType, typeof(LockstepGameStateOptionsData)))
            {
                Debug.LogError($"[Lockstep] The {ubType.Name} class, a {nameof(LockstepGameStateOptionsUI)}, "
                    + $"is trying to use the class '{currentOptionsType.Name}' for"
                    + $"{nameof(LockstepGameStateOptionsUI.OptionsClassName)}, however said class does not "
                    + $"derive from {nameof(LockstepGameStateOptionsData)}.", optionsUI);
                return false;
            }

            knownValidTypes.Add(ubType);
            return true;
        }

        private static void EarlyChecks(System.Type ubType)
        {
            if (ubType.IsAbstract || !EditorUtil.DerivesFrom(ubType, typeof(LockstepGameStateOptionsUI)))
                return;

            FieldInfo currentOptionsField = EditorUtil.GetFieldIncludingBase(ubType, "currentOptions", PrivateAndPublicFlags);
            FieldInfo optionsToValidateField = EditorUtil.GetFieldIncludingBase(ubType, "optionsToValidate", PrivateAndPublicFlags);
            if (currentOptionsField == null || optionsToValidateField == null)
            {
                Debug.LogError($"[Lockstep] The {ubType.Name} class, a {nameof(LockstepGameStateOptionsUI)}, "
                    + $"is missing the field 'currentOptions' or 'optionsToValidate' or both. "
                    + $"Said fields must be defined and both use the field type as described by "
                    + $"{nameof(LockstepGameStateOptionsUI.OptionsClassName)}. They can be private.");
            }
        }
    }
}
