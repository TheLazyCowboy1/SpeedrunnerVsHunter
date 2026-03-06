using Menu.Remix.MixedUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace EasyModSetup;

/// <summary>
/// A tool to easily add remix configs. Just add the Config attribute to a public field (either static or instance).
/// </summary>
public abstract class AutoConfigOptions : OptionInterface
{
    #region Hooks
    private static bool HooksApplied = false;
    public static void ApplyHooks()
    {
        if (!HooksApplied)
        {
            On.OptionInterface.ConfigHolder.Reload += ConfigHolder_Reload;
            On.OptionInterface.ConfigHolder.Save += ConfigHolder_Save;
        }
        HooksApplied = true;
    }

    public static void RemoveHooks()
    {
        if (HooksApplied)
        {
            On.OptionInterface.ConfigHolder.Reload -= ConfigHolder_Reload;
            On.OptionInterface.ConfigHolder.Save -= ConfigHolder_Save;
        }
        HooksApplied = false;
    }

    private static void ConfigHolder_Reload(On.OptionInterface.ConfigHolder.orig_Reload orig, ConfigHolder self)
    {
        orig(self);

        (SimplerPlugin.ConfigOptions as AutoConfigOptions).SetValues();
    }

    private static void ConfigHolder_Save(On.OptionInterface.ConfigHolder.orig_Save orig, ConfigHolder self)
    {
        orig(self);

        (SimplerPlugin.ConfigOptions as AutoConfigOptions).SetValues();
    }
    #endregion

    /// <summary>
    /// Adds this field to the remix/config menu for your mod in the specified tab.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class Config : Attribute
    {
        public string Tab, Label = "", Desc = "";
        public bool rightSide = false;
        public bool hide = false;
        public float width = -1f, spaceBefore = 0f, spaceAfter = 0f, height = -1f, extraMargin = 0;
        /// <summary>
        /// Used for float configs
        /// </summary>
        public byte precision = 2;
        /// <summary>
        /// Used for string configs. Makes the config a dropdown choice-selection box instead of a textbox.
        /// </summary>
        public string[] dropdownOptions = null;
        public Config(string tab, string label = "", string desc = "") : base()
        {
            Tab = tab;
            Label = label;
            Desc = desc;
        }
    }

    /// <summary>
    /// Limits the range of int and float configs
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class LimitRange : Attribute
    {
        public float Min, Max;
        public LimitRange(float min, float max) : base()
        {
            Min = min;
            Max = max;
        }
    }

    /// <summary>
    /// Defines formatting info, mostly default config spacing, for the tab.
    /// </summary>
    public struct TabInfo
    {
        public string name;
        public float startHeight = 550f, spacing = 40f, leftMargin = 50f,
            textOffset = 90f, updownWidth = 80f, checkboxOffset = 50f,
            rightMargin = 300f, defaultHeight = 25f;
        public TabInfo(string name)
        {
            this.name = name;
        }
    }

    public AutoConfigOptions(TabInfo[] tabs)
    {
        TabInfos = tabs;

        List<ConfigInfo> configs = new();

        FieldInfo[] fields = GetType().GetFields();
        foreach (FieldInfo info in fields)
        {
            try
            {
                Config att = info.GetCustomAttribute<Config>();
                if (att != null)
                {
                    ConfigurableBase configBase = (ConfigurableBase)typeof(ConfigHolder).GetMethods().First(m => m.Name == nameof(ConfigHolder.Bind)).MakeGenericMethod(info.FieldType)
                        .Invoke(config, new object[] { info.Name, info.GetValue(this), null });

                    configBase.info.acceptable = AcceptableForConfig(info.Name);

                    LimitRange rangeAtt = info.GetCustomAttribute<LimitRange>();
                    if (rangeAtt != null)
                    {
                        if (info.FieldType == typeof(int))
                            configBase.info.acceptable = (ConfigAcceptableBase)Activator.CreateInstance(typeof(ConfigAcceptableRange<>).MakeGenericType(info.FieldType), (int)rangeAtt.Min, (int)rangeAtt.Max);
                        else
                            configBase.info.acceptable = (ConfigAcceptableBase)Activator.CreateInstance(typeof(ConfigAcceptableRange<>).MakeGenericType(info.FieldType), rangeAtt.Min, rangeAtt.Max);
                    }

                    configs.Add(new() { config = configBase, tab = att.Tab, label = att.Label.Length > 0 ? att.Label : FieldNameToLabel(info.Name), desc = att.Desc,
                        hide = att.hide, rightSide = att.rightSide, width = att.width, spaceBefore = att.spaceBefore,
                        spaceAfter = att.spaceAfter, height = att.height, extraMargin = att.extraMargin,
                        precision = att.precision, dropdownOptions = att.dropdownOptions
                    });
                }
            } catch (Exception ex) { SimplerPlugin.Error(ex); }
        }

        ConfigInfos = configs.ToArray();
        SimplerPlugin.Log("Found " + ConfigInfos.Length + " configs");
    }

    private static string FieldNameToLabel(string n)
    {
        for (int i = n.Length-1; i >= 1; i--)
        {
            if (char.IsUpper(n[i]) && !char.IsUpper(n[i - 1]))
                n = n.Insert(i, " "); //insert a space before uppercase characters, if they are after lowercase characters
        }
        return n;
    }

    private struct ConfigInfo
    {
        public ConfigurableBase config;
        public string tab;
        public string label;
        public string desc;
        public bool rightSide;
        public bool hide;
        public float width, spaceBefore, spaceAfter, height, extraMargin;
        public byte precision;
        public string[] dropdownOptions;
    }

    private ConfigInfo[] ConfigInfos;
    public TabInfo[] TabInfos;

    public override void Initialize()
    {
        Tabs = new OpTab[TabInfos.Length];
        for (int i = 0; i < TabInfos.Length; i++)
        {
            TabInfo tInfo = TabInfos[i];
            string name = tInfo.name;
            Tabs[i] = new(this, name);

            float y = tInfo.startHeight;
            bool lastWasLeft = false;

            foreach (ConfigInfo cInfo in ConfigInfos)
            {
                try
                {
                    if (cInfo.tab == name)
                    {
                        float x = (cInfo.rightSide ? tInfo.rightMargin : tInfo.leftMargin) + cInfo.extraMargin;
                        float w = cInfo.width >= 0 ? cInfo.width : tInfo.updownWidth;
                        float h = cInfo.height >= 0 ? cInfo.height : tInfo.defaultHeight;
                        float t = tInfo.textOffset + w - tInfo.updownWidth; //updownWidth is the default

                        if (lastWasLeft && cInfo.rightSide)
                            y += tInfo.spacing; //keep on same height... a janky method to do so, but oh well
                        lastWasLeft = !cInfo.rightSide;

                        y -= cInfo.spaceBefore; //add extra space

                        UIelement el;
                        if (cInfo.config is Configurable<bool> cb)
                            el = new OpCheckBox(cb, x + tInfo.checkboxOffset, y);
                        else if (cInfo.config is Configurable<float> cf)
                            el = new OpUpdown(cf, new(x, y), w, cInfo.precision);
                        else if (cInfo.config is Configurable<int> ci)
                            el = new OpUpdown(ci, new(x, y), w);
                        else if (cInfo.config is Configurable<KeyCode> ck)
                            el = new OpKeyBinder(ck, new(x, y), new(w, h));
                        else if (cInfo.config is Configurable<string> cs)
                        {
                            if (cInfo.dropdownOptions != null)
                                el = new OpComboBox(cs, new(x, y), w, cInfo.dropdownOptions);
                            else
                                el = new OpTextBox(cs, new(x, y), w);
                        }
                        else
                        {
                            SimplerPlugin.Error("This config type is not yet supported: " + cInfo.config.GetType().FullName);
                            continue;
                        }
                        el.description = cInfo.desc;

                        Tabs[i].AddItems(new OpLabel(x + t, y, cInfo.label), el);
                        y -= tInfo.spacing + cInfo.spaceAfter;
                    }
                }
                catch (Exception ex) { SimplerPlugin.Error("Error with " + cInfo.label); SimplerPlugin.Error(ex); }
            }

            //Tabs[i].items = Tabs[i].items.Reverse().ToHashSet(); //attempt to make dropdowns not be behind the configs
            //move comboBoxes to the front
            foreach (UIelement el in Tabs[i].items)
            {
                if (el is OpComboBox comboBox)
                    comboBox.myContainer.MoveToFront();
            }
        }

        MenuInitialized();
    }

    /// <summary>
    /// Use to add any additional elements, such as buttons
    /// </summary>
    public virtual void MenuInitialized()
    {

    }

    /// <summary>
    /// Use this to add a custom ConfigAcceptableBase to your config
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public virtual ConfigAcceptableBase AcceptableForConfig(string id)
    {
        return null;
    }


    public void SetValues()
    {
        Type type = GetType();
        foreach (ConfigInfo info in ConfigInfos)
        {
            try
            {
                type.GetField(info.config.key).SetValue(this, info.config.BoxedValue);
            } catch (Exception ex) { SimplerPlugin.Error(ex); }
        }
        SimplerPlugin.Log("Set config values for " + mod?.id);
    }

}