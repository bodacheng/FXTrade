using System;
using System.Collections.Generic;
using TestFXTrade.Fx.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace TestFXTrade.Fx.UI
{
    public sealed class UsdJpyTrendLineGraphic : MaskableGraphic
    {
        private readonly List<Candle> candles = new List<Candle>();

        public void SetCandles(IReadOnlyList<Candle> source)
        {
            candles.Clear();

            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    candles.Add(source[i]);
                }
            }

            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect rect = rectTransform.rect;
            DrawGrid(vh, rect);

            if (candles.Count < 2)
            {
                return;
            }

            double min = double.MaxValue;
            double max = double.MinValue;
            for (int i = 0; i < candles.Count; i++)
            {
                min = Math.Min(min, candles[i].Close);
                max = Math.Max(max, candles[i].Close);
            }

            if (Math.Abs(max - min) < 0.0001d)
            {
                max += 0.01d;
                min -= 0.01d;
            }

            Vector2 previous = ToPoint(0, candles[0].Close, min, max, rect);
            Color32 lineColor = candles[candles.Count - 1].Close >= candles[0].Close
                ? new Color32(75, 201, 133, 255)
                : new Color32(232, 94, 111, 255);

            for (int i = 1; i < candles.Count; i++)
            {
                Vector2 current = ToPoint(i, candles[i].Close, min, max, rect);
                AddLine(vh, previous, current, 3.5f, lineColor);
                previous = current;
            }
        }

        private void DrawGrid(VertexHelper vh, Rect rect)
        {
            Color32 gridColor = new Color32(255, 255, 255, 24);

            for (int i = 1; i < 4; i++)
            {
                float y = rect.yMin + (rect.height * i / 4f);
                AddLine(vh, new Vector2(rect.xMin, y), new Vector2(rect.xMax, y), 1f, gridColor);
            }

            for (int i = 1; i < 5; i++)
            {
                float x = rect.xMin + (rect.width * i / 5f);
                AddLine(vh, new Vector2(x, rect.yMin), new Vector2(x, rect.yMax), 1f, gridColor);
            }
        }

        private Vector2 ToPoint(int index, double close, double min, double max, Rect rect)
        {
            float x = rect.xMin + (rect.width * index / Math.Max(1f, candles.Count - 1f));
            float normalized = (float)((close - min) / (max - min));
            float y = rect.yMin + (normalized * rect.height);
            return new Vector2(x, y);
        }

        private static void AddLine(VertexHelper vh, Vector2 start, Vector2 end, float width, Color32 color)
        {
            Vector2 direction = (end - start).normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x) * (width * 0.5f);

            int index = vh.currentVertCount;
            vh.AddVert(start - normal, color, Vector2.zero);
            vh.AddVert(start + normal, color, Vector2.zero);
            vh.AddVert(end + normal, color, Vector2.zero);
            vh.AddVert(end - normal, color, Vector2.zero);
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index + 2, index + 3, index);
        }
    }
}
