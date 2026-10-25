using System;
using System.Text;
using Il2CppInterop.Runtime;

namespace TOHE.Patches;

// Originally code by Gurge44. Reference: https://github.com/Gurge44/EndlessHostRoles/blob/main/Patches/TextBoxPatch.cs

[HarmonyPatch(typeof(TextBoxTMP), nameof(TextBoxTMP.SetText))]
class TextBoxTMPSetTextPatch
{
    private static readonly IntPtr TextField = IL2CPP.GetIl2CppField(Il2CppClassPointerStore<TextBoxTMP>.NativeClassPtr, "text");
    private static readonly IntPtr CompositionField = IL2CPP.GetIl2CppField(Il2CppClassPointerStore<TextBoxTMP>.NativeClassPtr, "compoText");

    // Allow Unicode text, but keep non-text control characters out of the TMP output.
    public static bool Prefix(TextBoxTMP __instance, [HarmonyArgument(0)] string input, [HarmonyArgument(1)] string inputCompo = "")
    {
        // Match the live chat field itself; other text boxes (including cloned search fields) stay vanilla.
        if (!HudManager.InstanceExists) return true;
        var chat = HudManager.Instance.Chat;
        if (!chat || !chat.freeChatField || chat.freeChatField.textArea != __instance) return true;

        input ??= "";
        inputCompo ??= "";

        bool flag = false;
        char ch = ' ';
        __instance.AdjustCaretPosition(input.Length - __instance.text.Length);
        __instance.tempTxt.Clear();

        foreach (char c in input)
        {
            char upperInvariant = c;
            if (ch == ' ' && upperInvariant == ' ')
            {
                __instance.AdjustCaretPosition(-1);
            }
            else
            {
                switch (upperInvariant)
                {
                    case '\r':
                    case '\n':
                        flag = true;
                        __instance.AdjustCaretPosition(-1);
                        continue;
                    case '\b':
                        __instance.tempTxt.Length = Math.Max(__instance.tempTxt.Length - 1, 0);
                        __instance.AdjustCaretPosition(-2);
                        continue;
                }

                if (char.IsControl(upperInvariant))
                {
                    __instance.AdjustCaretPosition(-1);
                    continue;
                }

                if (__instance.ForceUppercase) upperInvariant = char.ToUpperInvariant(upperInvariant);
                __instance.tempTxt.Append(upperInvariant);
                ch = upperInvariant;
            }
        }

        if (!__instance.tempTxt.ToString().Equals(DestroyableSingleton<TranslationController>.Instance.GetString(StringNames.EnterName), StringComparison.OrdinalIgnoreCase) && __instance.characterLimit > 0)
        {
            int length = __instance.tempTxt.Length;
            __instance.tempTxt.Length = Math.Min(__instance.tempTxt.Length, __instance.characterLimit);
            __instance.AdjustCaretPosition(-(length - __instance.tempTxt.Length));
        }

        input = __instance.tempTxt.ToString();
        if (!string.IsNullOrEmpty(inputCompo))
        {
            // IME composition is displayed separately and must not move the committed-text caret.
            var composition = new StringBuilder(inputCompo.Length);
            foreach (char c in inputCompo)
            {
                if (!char.IsControl(c)) composition.Append(c);
            }
            inputCompo = composition.ToString();
        }

        if (!input.Equals(__instance.text) || !inputCompo.Equals(__instance.compoText))
        {
            // The generated interop string setters use a raw pointer store in 1.5.1.
            // Use IL2CPP's reference-field API so incremental GC tracks both writes.
            var nativeInput = IL2CPP.Il2CppObjectBaseToPtrNotNull(__instance);
            IL2CPP.il2cpp_field_set_value_object(nativeInput, TextField, IL2CPP.ManagedStringToIl2Cpp(input));
            IL2CPP.il2cpp_field_set_value_object(nativeInput, CompositionField, IL2CPP.ManagedStringToIl2Cpp(inputCompo));
            string str = __instance.text;
            string compoText = __instance.compoText;

            if (__instance.Hidden)
            {
                str = "";
                for (int index = 0; index < __instance.text.Length; ++index)
                    str += "*";
            }

            __instance.outputText.text = str + compoText;
            __instance.outputText.ForceMeshUpdate(true, true);
            if (__instance.keyboard != null) __instance.keyboard.text = __instance.text;
            __instance.OnChange.Invoke();
        }

        if (flag) __instance.OnEnter.Invoke();
        __instance.SetPipePosition();

        return false;
    }
}

//Thanks https://github.com/NuclearPowered/Reactor/blob/master/Reactor/Patches/Fixes/CursorPosPatch.cs

//2024.8.13 break this

/// <summary>
/// "Fixes" an issue where empty TextBoxes have wrong cursor positions.
/// </summary>
/*[HarmonyPatch(typeof(TextMeshProExtensions), nameof(TextMeshProExtensions.CursorPos), typeof(TextMeshPro))]
[HarmonyPatch(typeof(TextMeshProExtensions), nameof(TextMeshProExtensions.CursorPos), typeof(TextMeshPro), typeof(int))]
internal static class CursorPosPatch
{
    public static bool Prefix(TextMeshPro self, ref Vector2 __result)
    {
        if (self.textInfo == null || self.textInfo.lineCount == 0 || self.textInfo.lineInfo[0].characterCount <= 0)
        {
            __result = self.GetTextInfo(" ").lineInfo.First().lineExtents.max;
            return false;
        }

        return true;
    }
} */



/* Originally by KARPED1EM. Reference: https://github.com/KARPED1EM/TownOfNext/blob/TONX/TONX/Patches/TextBoxPatch.cs */
/*[HarmonyPatch(typeof(TextBoxTMP))]
public class TextBoxPatch
{
    [HarmonyPatch(nameof(TextBoxTMP.SetText)), HarmonyPrefix]
    public static void ModifyCharacterLimit(TextBoxTMP __instance)
    {
        __instance.characterLimit = 1200;
    }
}*/
