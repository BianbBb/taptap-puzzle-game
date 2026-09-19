#if UNITY_EDITOR
using Aspose.PSD;
using Aspose.PSD.FileFormats.Psd;
using Aspose.PSD.FileFormats.Psd.Layers;
using Aspose.PSD.FileFormats.Psd.Layers.SmartObjects;
using UnityEngine;

namespace DGame.PSD2UGUI
{
    public enum PsdLayerType
    {
        Unknown = 0,
        Layer,
        BlackWhiteAdjustmentLayer,
        BrightnessContrastLayer,
        CmykChannelMixerLayer,
        ColorBalanceAdjustmentLayer,
        CurvesLayer,
        ExposureLayer,
        HueSaturationLayer,
        InvertAdjustmentLayer,
        LevelsLayer,
        PhotoFilterLayer,
        PosterizeLayer,
        RgbChannelMixerLayer,
        SelectiveColorLayer,
        ThresholdLayer,
        VibranceLayer,
        FillLayer,
        LayerGroup,
        SectionDividerLayer,
        ShapeLayer,
        SmartObjectLayer,
        TextLayer
    }

    public static class AsposePsdExtension
    {
        /// <summary>
        /// 把图层的 Opacity 透明度合并到像素透明度
        /// </summary>
        public static void MergeLayerOpacity(this Layer layer)
        {
            if (layer.Opacity >= 255) return;
            var pixelData = layer.LoadArgb32Pixels(layer.Bounds);
            for (int i = 0; i < pixelData.Length; i++)
            {
                var alpha = (byte)(pixelData[i] >> 24);
                var mergedAlpha = (byte)((alpha * layer.Opacity) / 255);
                pixelData[i] = (int)((pixelData[i] & 0x00FFFFFF) | (mergedAlpha << 24));
            }
            layer.SaveArgb32Pixels(layer.Bounds, pixelData);
        }

        /// <summary>
        /// 获取被 SmartObjectLayer 包裹的 Text 图层
        /// </summary>
        public static Layer GetSmartObjectInnerTextLayer(this SmartObjectLayer smartLayer)
        {
            var rawLayer = smartLayer?.LoadContents(null) as PsdImage;
            if (rawLayer != null && rawLayer.Layers.Length == 1)
            {
                var innerLayer = rawLayer.Layers[0];
                if (innerLayer != null && innerLayer.GetLayerType() == PsdLayerType.TextLayer)
                    return innerLayer;
            }
            return null;
        }

        /// <summary>
        /// 获取 PSD 图层类型枚举
        /// </summary>
        public static PsdLayerType GetLayerType(this Layer layer)
        {
            if (System.Enum.TryParse<PsdLayerType>(layer.GetType().Name, out var tp))
            {
                return tp;
            }
            Debug.LogWarning($"解析图层类型失败:{layer.GetType().Name}");
            return PsdLayerType.Layer;
        }

        /// <summary>
        /// 获取 PSD 图层的 Rect 边框(Unity 坐标系)
        /// </summary>
        public static Rect GetLayerRect(this Layer layer)
        {
            RectangleF psdRect = new RectangleF
            {
                Left = layer.Left,
                Right = layer.Right,
                Top = layer.Top,
                Bottom = layer.Bottom
            };
            var canvasSize = new Vector2Int(layer.Container.Size.Width, layer.Container.Size.Height);
            return PsdRect2UnityRect(psdRect, canvasSize);
        }

        public static Rect PsdRect2UnityRect(Rectangle psdRect, Vector2Int canvasSize)
        {
            return PsdRect2UnityRect(new RectangleF(psdRect.X, psdRect.Y, psdRect.Width, psdRect.Height), canvasSize);
        }

        public static Rect PsdRect2UnityRect(RectangleF psdRect, Vector2Int canvasSize)
        {
            float halfWidth = Mathf.Abs(psdRect.Right - psdRect.Left) * 0.5f;
            float halfHeight = Mathf.Abs(psdRect.Bottom - psdRect.Top) * 0.5f;
            Rect result = new Rect(
                psdRect.Left + halfWidth - canvasSize.x * 0.5f,
                canvasSize.y * 0.5f - (psdRect.Top + halfHeight),
                psdRect.Right - psdRect.Left,
                psdRect.Bottom - psdRect.Top);
            return result;
        }

        /// <summary>
        /// 修复当 LayerGroup 为第一层时, 对应 Bounds 错位
        /// </summary>
        public static Rectangle GetFixedLayerBounds(this Layer layerGroup)
        {
            if (layerGroup.GetLayerType() != PsdLayerType.LayerGroup)
            {
                return layerGroup.Bounds;
            }
            var subLayers = (layerGroup as LayerGroup).Layers;
            int minLeft = int.MaxValue;
            int minTop = int.MaxValue;
            int maxRight = int.MinValue;
            int maxBottom = int.MinValue;
            foreach (var item in subLayers)
            {
                var itemTp = item.GetLayerType();
                if (itemTp == PsdLayerType.Unknown || itemTp == PsdLayerType.LayerGroup || itemTp == PsdLayerType.SectionDividerLayer) continue;
                if (item.Left < minLeft) minLeft = item.Left;
                if (item.Top < minTop) minTop = item.Top;
                if (item.Right > maxRight) maxRight = item.Right;
                if (item.Bottom > maxBottom) maxBottom = item.Bottom;
            }
            return new Rectangle
            {
                Top = minTop,
                Left = minLeft,
                Right = maxRight,
                Bottom = maxBottom
            };
        }

        public static UnityEngine.Color ToUnityColor(this Aspose.PSD.Color color)
        {
            return new UnityEngine.Color(color.R, color.G, color.B, color.A) / 255f;
        }

        public static UnityEngine.Color ToUnityColor(this Aspose.PSD.Color color, int alpha)
        {
            return new UnityEngine.Color(color.R, color.G, color.B, alpha) / 255f;
        }
    }
}
#endif
