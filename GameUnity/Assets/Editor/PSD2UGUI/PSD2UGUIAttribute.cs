#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Aspose.PSD.FileFormats.Psd;
using Aspose.PSD.FileFormats.Psd.Layers;
using Aspose.PSD.FileFormats.Psd.Layers.FillLayers;
using Aspose.PSD.FileFormats.Psd.Layers.FillSettings;
using Aspose.PSD.FileFormats.Psd.Layers.LayerEffects;
using UnityEditor;
using UnityEngine;
using Color = UnityEngine.Color;

#pragma warning disable CS0612
#pragma warning disable CS0618

namespace DGame.PSD2UGUI
{
    public enum FontAlignType
    {
        Left,
        Center,
        Right,
    }

    public enum UIType
    {
        None,
        Text,
        Image,
    }

    /// <summary>
    /// PSD 图层 → UI 属性
    /// </summary>
    public class PSD2UGUIAttribute
    {
        public UIType uiType = UIType.None;

        // 位置/大小
        public Vector2 pos;
        public Vector2 size;
        public bool isLocalPos;

        // 颜色
        public Color color = Color.white;
        public List<Color> gradient;
        public int gradientAngle;

        // 图片
        public string imgName;
        public string imgPath;
        public bool isRawImage;
        public bool isButton;
        public bool isHide;

        // 文字
        public string textStr = "Text";
        public string fontName;
        public int fontSize;
        public FontStyle fontStyle = FontStyle.Normal;

        public bool shadow = false;
        public Color shadowColor = Color.black;
        public Vector2 shadowDis;

        public bool outline = false;
        public int outlineSize;
        public Color outlineColor = Color.black;

        public FontAlignType alignType = FontAlignType.Center;

        /// <summary>
        /// 从 PSD 图层提取属性
        /// </summary>
        public void Parser(Layer layer)
        {
            Rect rect = layer.GetLayerRect();
            size = rect.size;
            pos = rect.position;

            bool setSelfColor = true;
            foreach (var item in layer.BlendingOptions.Effects)
            {
                if (!item.IsVisible) continue;

                if (item is StrokeEffect stroke) // 描边
                {
                    outline = true;
                    outlineSize = stroke.Size;
                    if (stroke.FillSettings is ColorFillSettings settings)
                    {
                        outlineColor = settings.Color.ToUnityColor(stroke.Opacity);
                    }
                    else if (stroke.FillSettings is GradientFillSettings gradientSettings)
                    {
                        Debug.LogWarning("渐变描边暂不支持");
                        outlineColor = gradientSettings.Color.ToUnityColor(stroke.Opacity);
                    }
                }
                if (item is DropShadowEffect dropShadow) // 阴影
                {
                    shadow = true;
                    shadowColor = dropShadow.Color.ToUnityColor(dropShadow.Opacity);
                    switch (dropShadow.Angle)
                    {
                        case 0: shadowDis = Vector2.left; break;
                        case 180:
                        case -180: shadowDis = Vector2.right; break;
                        case 90: shadowDis = Vector2.down; break;
                        case -90: shadowDis = Vector2.up; break;
                        default:
                            var offsetX = Mathf.Cos((dropShadow.Angle + 180) * (Mathf.PI / 180));
                            var offsetY = Mathf.Sin((dropShadow.Angle + 180) * (Mathf.PI / 180));
                            shadowDis = new Vector2(offsetX, offsetY);
                            break;
                    }
                    shadowDis *= dropShadow.Distance;
                }
                if (item is ColorOverlayEffect colorOverLay) // 颜色叠加
                {
                    setSelfColor = false;
                    if (colorOverLay.BlendMode == Aspose.PSD.FileFormats.Core.Blending.BlendMode.Normal)
                    {
                        int alpha = (int)(colorOverLay.Opacity * ((float)layer.Opacity / 255));
                        color = colorOverLay.Color.ToUnityColor(alpha);
                    }
                }
                if (item is GradientOverlayEffect gradientOverLay) // 渐变叠加
                {
                    setSelfColor = false;
                    var settings = gradientOverLay.Settings;
                    if (settings.ColorPoints.Length > 0)
                    {
                        gradient = new List<Color>();
                        foreach (var icolor in settings.ColorPoints)
                        {
                            gradient.Add(icolor.Color.ToUnityColor());
                        }
                        if (settings.Reverse) gradient.Reverse();
                        gradientAngle = (int)settings.Angle;
                    }
                }
            }

            var curLayerType = layer.GetLayerType();
            if (curLayerType == PsdLayerType.Layer || curLayerType == PsdLayerType.SmartObjectLayer ||
                curLayerType == PsdLayerType.ShapeLayer || curLayerType == PsdLayerType.FillLayer)
            {
                uiType = UIType.Image;
                string pattern = @"[^a-zA-Z0-9_].*";
                string result = Regex.Replace(layer.DisplayName, pattern, "");
                imgName = result;
                if (string.IsNullOrWhiteSpace(result))
                {
                    isHide = true;
                }
                FindImage();
                if (setSelfColor)
                {
                    if (curLayerType == PsdLayerType.ShapeLayer)
                    {
                        ShapeLayer shapeLayer = layer as ShapeLayer;
                        if (shapeLayer.Fill is ColorFillSettings setting)
                        {
                            color = setting.Color.ToUnityColor(layer.Opacity);
                        }
                    }
                    else if (curLayerType == PsdLayerType.FillLayer)
                    {
                        FillLayer fillLayer = layer as FillLayer;
                        if (fillLayer.FillSettings is ColorFillSettings setting)
                        {
                            color = setting.Color.ToUnityColor(layer.Opacity);
                        }
                    }
                    else
                    {
                        color.a = layer.Opacity / 255f;
                    }
                }
            }
            else if (curLayerType == PsdLayerType.TextLayer)
            {
                TextLayer textLayer = layer as TextLayer;
                uiType = UIType.Text;

                var textStyle = textLayer.TextData.Items[0].Style;
                if (textStyle.FauxBold && textStyle.FauxItalic)
                {
                    fontStyle = FontStyle.BoldAndItalic;
                }
                else
                {
                    if (textStyle.FauxBold) fontStyle = FontStyle.Bold;
                    if (textStyle.FauxItalic) fontStyle = FontStyle.Italic;
                }
                fontSize = Mathf.RoundToInt((float)(textLayer.Font.Size * textLayer.TransformMatrix[0]));
                if (setSelfColor)
                {
                    color = textLayer.TextColor.ToUnityColor(layer.Opacity);
                }

                textStr = "";
                Aspose.PSD.Color lastColor = textLayer.TextColor;
                foreach (var item in textLayer.TextData.Items)
                {
                    if (item.Style.FillColor != lastColor)
                    {
                        string colorStr = ColorUtility.ToHtmlStringRGB(item.Style.FillColor.ToUnityColor());
                        textStr += $"<color=#{colorStr}>{item.Text}</color>";
                    }
                    else
                    {
                        textStr += item.Text;
                    }
                    if (string.IsNullOrEmpty(fontName) || fontName == "AachenBT-Roman")
                    {
                        fontName = RemoveBom(item.Style.FontName);
                    }
                }
                if (textStr.EndsWith("\r"))
                {
                    textStr = textStr.Substring(0, textStr.Length - 1);
                }

                JustificationMode aligan = textLayer.TextData.Items[0].Paragraph.Justification;
                switch (aligan)
                {
                    case JustificationMode.Left: alignType = FontAlignType.Left; break;
                    case JustificationMode.Right: alignType = FontAlignType.Right; break;
                    case JustificationMode.Center: alignType = FontAlignType.Center; break;
                }
            }
            else if (curLayerType == PsdLayerType.SectionDividerLayer || curLayerType == PsdLayerType.LayerGroup)
            {
                uiType = UIType.None;
            }
            else
            {
                Debug.Log("暂不支持的图层类型:" + curLayerType);
                uiType = UIType.None;
            }
        }

        private string RemoveBom(string input)
        {
            if (!string.IsNullOrEmpty(input) && input[0] == '﻿')
            {
                return input.Substring(1);
            }
            return input;
        }

        /// <summary>
        /// 在配置的资源文件夹中查找匹配图片
        /// </summary>
        private void FindImage()
        {
            if (isHide || string.IsNullOrEmpty(imgName)) return;
            var settings = PSD2UGUISettings.Instance;
            if (imgName.IndexOf("button", StringComparison.OrdinalIgnoreCase) >= 0 ||
                imgName.IndexOf("btn", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                isButton = true;
            }

            var spriteFolders = ExistingFolders(settings.spriteSearchFolders);
            string[] guidArr = spriteFolders.Length > 0
                ? AssetDatabase.FindAssets($"{imgName} t:Sprite", spriteFolders)
                : Array.Empty<string>();

            if (guidArr.Length <= 0)
            {
                var textureFolders = ExistingFolders(settings.textureSearchFolders);
                if (textureFolders.Length > 0)
                {
                    guidArr = AssetDatabase.FindAssets($"{imgName} t:Texture", textureFolders);
                    if (guidArr.Length > 0) isRawImage = true;
                }
            }

            foreach (var item in guidArr)
            {
                string path = AssetDatabase.GUIDToAssetPath(item);
                string name = Path.GetFileNameWithoutExtension(path);
                if (name == imgName)
                {
                    imgPath = path;
                    break;
                }
            }
        }

        private static string[] ExistingFolders(string[] folders)
        {
            if (folders == null || folders.Length == 0) return Array.Empty<string>();
            var result = new List<string>(folders.Length);
            foreach (var f in folders)
            {
                if (!string.IsNullOrEmpty(f) && AssetDatabase.IsValidFolder(f)) result.Add(f);
            }
            return result.ToArray();
        }

        public override string ToString()
        {
            return $"类型:{uiType} 位置:{pos} 大小:{size} 颜色:{color} 图片:{imgName} 字号:{fontSize} 文本:{textStr}";
        }
    }
}
#endif
