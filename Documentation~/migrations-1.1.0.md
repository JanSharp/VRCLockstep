
# Updating to 1.1.0

## Initialization Changes

- `IsInitialized` is no longer `true` inside of `OnInit` and APIs requiring `IsInitialized` to be `true` can thus no longer be used inside of `OnInit` either.
- `LockstepIsInitialized` has been added, which is `true` inside of `OnInit`. Input actions and delayed events check this property, which is to say they can still be sent inside of `OnInit`.
- `OnInitFinished` has been added, which runs immediately after `OnInit`, and `IsInitialized` is `true` inside of `OnInitFinished`.
- `OnInit` has been changed to be allowed to be spread out across frames, see `FlagToContinueNextFrame`.
- `OnInitFinished` cannot be spread out across frames, thus it is a guarantee that by the end of the frame this event gets raised in all game states are fully initialized.

With all that in mind updating can be a matter of not having to change anything, having to move some logic from `OnInit` to `OnInitFinished` or changing some `IsInitialized` checks to `LockstepIsInitialized`.

There are 2 reasons for this change:

- `IsInitialized` is now a trustworthy method of checking if all game states have run their `OnInit` handlers. Technically game states may not be finished initializing if they rely on `OnInitFinished`, however the majority of game states can and should finish initialization inside of `OnInit`. And by the end of the frame they are all guaranteed to be finished initializing.
- `OnInit` gaining the ability to be spread out across frames enables having expensive initialization logic, without causing lag spikes or even risking running into the 10 second Udon time out.

## Catching Up Changes

`OnClientBeginCatchUp` has been changed the exact same way as `OnInit` in regards to `IsInitialized`, `LockstepIsInitialized` and the ability to be spread out across frames.

`OnPostClientBeginCatchUp` has been added, akin to `OnInitFinished`.

## Import Finishing Changes

`OnImportFinishingUp` has been added. Inside of it `IsImporting` is still `true`, unlike `OnImportFinished`. Also unlike `OnImportFinished`, `OnImportFinishingUp` can be spread out across frames, see `FlagToContinueNextFrame`.

It would be good to move logic to `OnImportFinishingUp` where possible, as it is going to reduce the amount of logic run in a single frame, reducing lag spikes, as well as making `OnImportFinished` a more useful notification of an import being finished.

`OnImportFinished` did not receive any breaking changes.

## Export Format Changes

The export format has been changed to have both a lockstep export signature as well as an internal data version. There's a few reasons:

- Further ensures invalid data does not get attempted to be parsed, which would throw exceptions
- Enables other tools to detect some data being a lockstep export string
- Makes it possible for lockstep to change its internal data structure while keeping the ability to import older data

However the introduction of this prevents importing previously exported strings, thus this is a breaking change.

If it is desired to import one of said old strings:

- Make sure to have the `com.jansharp.common` package installed
- Create the `Assets/Editor/LockstepUpdate.cs` file (see foldout below)
- In Unity go to Tools => Lockstep Update
- Paste an old export string into the input field
- Hit the convert button
- Copy the export string from the output field
- This export string is now valid for the new format and can be imported
- Delete the `Assets/Editor/LockstepUpdate.cs` again, it no longer serves any purpose

<details>
<summary><b>Assets/Editor/LockstepUpdate.cs</b></summary>

```cs
using UnityEditor;
using UnityEngine;

namespace JanSharp
{
    public class LockstepUpdateWindow : EditorWindow
    {
        [SerializeField] string input;
        [SerializeField] string output;
        SerializedObject so;
        SerializedProperty inputProp;
        SerializedProperty outputProp;

        private byte[] lockstepExportHeader = new byte[] { 0x20, 0x23, 0x05, 0x24, 0x6c, 0x73, 0x65, 0x3a, 0x00, 0x00, 0x00, 0x00 };
        private static uint[] crc32LookupCache;

        [MenuItem("Tools/Lockstep Update", priority = 1001)]
        public static void CreateLockstepUpdateWindow()
        {
            // This method is called when the user selects the menu item in the Editor
            EditorWindow wnd = GetWindow<LockstepUpdateWindow>();
            wnd.titleContent = new GUIContent("Lockstep Update");
        }

        private void OnEnable()
        {
            so = new SerializedObject(this);
            inputProp = so.FindProperty(nameof(input));
            outputProp = so.FindProperty(nameof(output));
        }

        private void OnGUI()
        {
            so.Update();
            EditorGUILayout.PropertyField(inputProp);

            if (GUILayout.Button("Convert to Lockstep 1.1.0 export format"))
                if (!Base64.TryDecode(inputProp.stringValue, out byte[] data) || data.Length < 4)
                    outputProp.stringValue = "Malformed input";
                else
                {
                    byte[] newData = new byte[data.Length + 12];
                    System.Array.Copy(lockstepExportHeader, newData, 12);
                    System.Array.Copy(data, 0, newData, 12, data.Length - 4);
                    int size = newData.Length - 4;
                    uint crc = CRC32.Compute(ref crc32LookupCache, newData, 0, size);
                    DataStream.Write(ref newData, ref size, crc);
                    outputProp.stringValue = Base64.Encode(newData);
                }

            EditorGUILayout.PropertyField(so.FindProperty(nameof(output)));
            so.ApplyModifiedProperties();
        }
    }
}
```

</details>
