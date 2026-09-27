// 职责：[PerformanceId] 字段的 PropertyDrawer——从 Addressables「Performance」组的已登记地址
// 里画下拉选择，避免动画师手打演出 id 打错字；末尾提供「手动输入…」切回文本框，
// 组不存在或当前值未登记时也退回文本框并给出红字提示。
// 为什么新建（复用 → 扩展 → 新建）：工程里没有先例——其余 PropertyDrawer 都是就地画值，
// 没有一个是「从 Addressables 组反查地址列表做下拉」的，找不到可扩展的宿主类。

using System;
using System.Collections.Generic;
using System.Linq;
using Game.Performance;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Game.Editor.Performance
{
    /// <summary>
    /// 画 <see cref="PerformanceIdAttribute"/> 标记的 string 字段：
    /// Addressables「Performance」组能取到地址列表时画下拉（含「手动输入…」），
    /// 取不到或当前值不在列表里时画文本框 + 「未登记」红字提示。
    /// </summary>
    [CustomPropertyDrawer(typeof(PerformanceIdAttribute))]
    internal sealed class PerformanceIdDrawer : PropertyDrawer
    {
        private const string PerformanceGroupName = "Performance";
        private const string ManualInputOption = "（手动输入…）";
        private const float WarningWidth = 56f;

        // 记录「用户主动切到手动输入」的字段，键含目标对象实例 id，避免跨对象/跨组件串状态。
        private static readonly Dictionary<string, bool> ManualModeByKey = new Dictionary<string, bool>();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            string[] addresses = GetPerformanceAddresses(out bool groupFound);
            string currentValue = property.stringValue;
            string key = BuildKey(property);
            bool manualMode = ManualModeByKey.TryGetValue(key, out bool stored) && stored;
            bool registered = groupFound && Array.IndexOf(addresses, currentValue) >= 0;
            bool unregisteredValue = !string.IsNullOrEmpty(currentValue) && (!groupFound || !registered);

            if (manualMode || !groupFound || unregisteredValue)
            {
                DrawManualField(position, property, label, key, addresses, unregisteredValue);
            }
            else
            {
                DrawPopup(position, property, label, key, addresses, currentValue);
            }

            EditorGUI.EndProperty();
        }

        private static void DrawPopup(
            Rect position,
            SerializedProperty property,
            GUIContent label,
            string key,
            string[] addresses,
            string currentValue)
        {
            string[] options = new string[addresses.Length + 1];
            Array.Copy(addresses, options, addresses.Length);
            options[addresses.Length] = ManualInputOption;

            int currentIndex = Array.IndexOf(addresses, currentValue);
            int displayIndex = currentIndex < 0 ? 0 : currentIndex;

            EditorGUI.BeginChangeCheck();
            int selected = EditorGUI.Popup(position, label.text, displayIndex, options);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            if (selected == addresses.Length)
            {
                ManualModeByKey[key] = true;
                return;
            }

            property.stringValue = options[selected];
        }

        private static void DrawManualField(
            Rect position,
            SerializedProperty property,
            GUIContent label,
            string key,
            string[] addresses,
            bool showWarning)
        {
            Rect fieldRect = position;
            Rect warningRect = default;
            if (showWarning)
            {
                fieldRect.width -= WarningWidth;
                warningRect = new Rect(fieldRect.xMax, position.y, WarningWidth, position.height);
            }

            string newValue = EditorGUI.TextField(fieldRect, label, property.stringValue);
            if (newValue != property.stringValue)
            {
                property.stringValue = newValue;
            }

            // 手动输入命中登记表后自动退出手动模式，下次绘制回到下拉。
            if (Array.IndexOf(addresses, newValue) >= 0)
            {
                ManualModeByKey.Remove(key);
            }

            if (showWarning)
            {
                Color previousColor = GUI.contentColor;
                GUI.contentColor = Color.red;
                EditorGUI.LabelField(warningRect, "未登记");
                GUI.contentColor = previousColor;
            }
        }

        private static string BuildKey(SerializedProperty property)
        {
            int targetId = property.serializedObject.targetObject != null
                ? property.serializedObject.targetObject.GetInstanceID()
                : 0;
            return targetId + ":" + property.propertyPath;
        }

        private static string[] GetPerformanceAddresses(out bool groupFound)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                groupFound = false;
                return Array.Empty<string>();
            }

            AddressableAssetGroup group = settings.FindGroup(PerformanceGroupName);
            if (group == null)
            {
                groupFound = false;
                return Array.Empty<string>();
            }

            groupFound = true;
            return group.entries
                .Select(entry => entry.address)
                .OrderBy(address => address, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
