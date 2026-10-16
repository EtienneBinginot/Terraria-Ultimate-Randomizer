using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;

namespace UltimateRandomizer.Common.Codex.Interface
{
	// A row that builds its content only when it first comes into view (grid rows: thousands of icons stay unbuilt)
	public interface ICodexLazy
	{
		void Materialize();
	}

	// The codex's scrolling list, virtualized: every row is kept, but only the rows crossing the visible area are
	// children. UIList recalculates all its rows and all their children on every frame drawn (UIList.DrawSelf), which
	// made a tab of thousands of icons lag; here only the visible rows are updated, drawn and recalculated, and only
	// when the view moves or the rows change.
	public class CodexList : UIElement
	{
		private readonly List<UIElement> items = new();
		private readonly List<float> tops = new();
		private float totalHeight;
		private UIScrollbar scrollbar;
		private bool layoutDirty = true;
		private int shownFirst, shownLast = -1;
		private float shownView = float.NaN;
		public float ListPadding = 6;

		// Off screen (tests build pages before the codex was ever shown), a typical height
		private const float FallbackHeight = 560;

		public CodexList()
		{
			OverflowHidden = true;
		}

		public int Count => items.Count;

		// Every row, shown or not
		public IReadOnlyList<UIElement> Items => items;

		public float ViewPosition
		{
			get => scrollbar?.ViewPosition ?? 0;
			set
			{
				if (scrollbar != null) scrollbar.ViewPosition = value;
			}
		}

		public void SetScrollbar(UIScrollbar bar)
		{
			scrollbar = bar;
			layoutDirty = true;
		}

		public void Clear()
		{
			items.Clear();
			Invalidate();
		}

		public void Add(UIElement item)
		{
			items.Add(item);
			Invalidate();
		}

		public void AddRange(IEnumerable<UIElement> range)
		{
			items.AddRange(range);
			Invalidate();
		}

		private void Invalidate()
		{
			RemoveAllChildren();
			shownFirst = 0;
			shownLast = -1;
			layoutDirty = true;
		}

		private float ViewHeight
		{
			get
			{
				float height = GetInnerDimensions().Height;
				return height > 50 ? height : FallbackHeight;
			}
		}

		private void Layout()
		{
			float height = GetInnerDimensions().Height;
			tops.Clear();
			float y = 0;
			for (int i = 0; i < items.Count; i++)
			{
				UIElement item = items[i];
				tops.Add(y);
				y += item.Height.GetValue(height) + item.MarginTop + item.MarginBottom + (i < items.Count - 1 ? ListPadding : 0);
			}
			totalHeight = y;
			scrollbar?.SetView(height, totalHeight);
		}

		// Shows the rows crossing the view and places them; nothing to do (no allocation) when the view did not move
		public void Sync()
		{
			if (layoutDirty)
			{
				layoutDirty = false;
				Layout();
				shownView = float.NaN;
			}
			float view = Math.Max(0, ViewPosition);
			if (view == shownView) return;
			shownView = view;

			int first = 0, last = -1;
			if (items.Count > 0)
			{
				first = tops.BinarySearch(view);
				if (first < 0) first = Math.Max(0, ~first - 1);
				float bottom = view + ViewHeight;
				last = first;
				while (last + 1 < items.Count && tops[last + 1] < bottom) last++;
			}
			for (int i = shownFirst; i <= shownLast; i++)
			{
				if (i < first || i > last) RemoveChild(items[i]);
			}
			for (int i = first; i <= last; i++)
			{
				UIElement item = items[i];
				if (i < shownFirst || i > shownLast)
				{
					(item as ICodexLazy)?.Materialize();
					Append(item);
				}
				item.Top.Set(tops[i] - view, 0);
			}
			shownFirst = first;
			shownLast = last;
			RecalculateChildren();
		}

		public override void Recalculate()
		{
			base.Recalculate();
			// The size may have changed: heights in percent and the scrollbar's view follow
			layoutDirty = true;
			Sync();
		}

		public override void Update(GameTime gameTime)
		{
			Sync();
			base.Update(gameTime);
		}

		// The scrollbar is dragged while it is drawn: the rows follow on the next frame drawn, like UIList's
		protected override void DrawSelf(SpriteBatch spriteBatch) => Sync();

		public override void ScrollWheel(UIScrollWheelEvent evt)
		{
			base.ScrollWheel(evt);
			if (scrollbar != null) scrollbar.ViewPosition -= evt.ScrollWheelValue;
		}
	}
}
