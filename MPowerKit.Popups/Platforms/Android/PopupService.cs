﻿using Android.App;
using Android.Content;

using AndroidX.ConstraintLayout.Widget;

using Microsoft.Maui.Platform;

using View = Android.Views.View;
using ViewGroup = Android.Views.ViewGroup;

namespace MPowerKit.Popups;

public partial class PopupService
{
    protected virtual partial void AttachToWindow(PopupPage page, IViewHandler pageHandler, Window parentWindow)
    {
        HandleAccessibility(true, page.DisableAndroidAccessibilityHandling, parentWindow);

        var activity = parentWindow.Handler.PlatformView as Android.App.Activity;

        var dv = activity?.Window?.DecorView as ViewGroup
            ?? throw new InvalidOperationException("DecorView of Activity not found");

        var handler = (pageHandler as IPlatformViewHandler)!;

        handler.PlatformView!.ViewAttachedToWindow += (s, e) =>
        {
            dv.Context!.HideKeyboard(dv);
        };

        handler.PlatformView.ViewDetachedFromWindow += (s, e) =>
        {
            dv.Context!.HideKeyboard(dv);
        };

        bool keyboardVisible = false;

        handler.PlatformView!.ViewTreeObserver!.GlobalLayout += (s, e) =>
        {
            var view = dv.FindViewById(Android.Resource.Id.Content);

            var r = new Android.Graphics.Rect();
            view!.GetWindowVisibleDisplayFrame(r);
            int screenHeight = view.RootView!.Height;

            // r.bottom is the position above soft keypad or device button.
            // if keypad is shown, the r.bottom is smaller than that before.
            int keypadHeight = screenHeight - r.Bottom;

            if (keypadHeight > screenHeight * 0.15)
            {
                if (!keyboardVisible)
                {
                    keyboardVisible = true;
                }
            }
            else
            {
                if (keyboardVisible)
                {
                    keyboardVisible = false;
                }
            }
        };

        handler.PlatformView.Touch += (s, e) =>
        {
            var view = (s as ViewGroup)!;

            if (page.Content is not null && view.ChildCount > 0)
            {
                var child = view.GetChildAt(0)!;

                var rawx = e.Event.GetX();
                var rawy = e.Event.GetY();
                var childx = child.GetX();
                var childy = child.GetY();

                if (rawx >= childx && rawx <= (child.Width + childx)
                    && rawy >= childy && rawy <= (child.Height + childy))
                {
                    if (keyboardVisible)
                    {
                        view.Context!.HideKeyboard(view);
                        view.FindFocus()?.ClearFocus();
                    }

                    e.Handled = true;
                    return;
                }
            }

            if (e.Event!.Action is Android.Views.MotionEventActions.Down)
            {
                if (!page.BackgroundInputTransparent && keyboardVisible)
                {
                    view.Context!.HideKeyboard(view);
                    view.FindFocus()?.ClearFocus();
                    e.Handled = true;
                    return;
                }
            }

            if (e.Event!.Action is Android.Views.MotionEventActions.Up)
            {
                page.SendBackgroundClick();
            }

            e.Handled = !page.BackgroundInputTransparent;
        };

        AddToVisualTree(page, handler, activity!);
    }

    protected virtual void AddToVisualTree(PopupPage page, IPlatformViewHandler handler, Activity activity)
    {
        var dv = activity?.Window?.DecorView as ViewGroup
            ?? throw new InvalidOperationException("DecorView of Activity not found");

        var view = !page.HasSystemPadding
            ? handler.PlatformView!
            : new ParentLayout(dv.Context!, dv, page);
        view.Elevation = 10000;

        dv.AddView(view);
    }

    protected virtual partial void DetachFromWindow(PopupPage page, IViewHandler pageHandler, Window parentWindow)
    {
        var handler = (pageHandler as IPlatformViewHandler)!;

        HandleAccessibility(false, page.DisableAndroidAccessibilityHandling, parentWindow);

        RemoveFromVisualTree(page, handler);
    }

    protected virtual void RemoveFromVisualTree(PopupPage page, IPlatformViewHandler handler)
    {
        if (page.HasSystemPadding)
        {
            var layout = (handler.PlatformView!.Parent as ParentLayout)!;
            layout.RemoveGlobalLayoutListener();
            layout.RemoveFromParent();
        }
        else handler.PlatformView!.RemoveFromParent();
    }

    //! important keeps reference to pages that accessibility has applied to. This is so accessibility can be removed properly when popup is removed. #https://github.com/LuckyDucko/Mopups/issues/93
    protected List<View?> AccessibilityViews { get; } = [];
    protected virtual void HandleAccessibility(bool showPopup, bool disableAccessibilityHandling, Window window)
    {
        if (disableAccessibilityHandling) return;

        if (showPopup)
        {
            var mainPage = window.Page;
            if (mainPage is null) return;

            AccessibilityViews.Add(mainPage.Handler?.PlatformView as View);

            if (mainPage.Navigation.NavigationStack.Count > 0)
            {
                AccessibilityViews.Add(mainPage.Navigation?.NavigationStack[^1]?.Handler?.PlatformView as View);
            }

            if (mainPage.Navigation!.ModalStack.Count > 0)
            {
                AccessibilityViews.Add(mainPage.Navigation?.ModalStack[^1]?.Handler?.PlatformView as View);
            }
        }

        foreach (var view in AccessibilityViews)
        {
            if (view is null) continue;

            // Screen reader
            view.ImportantForAccessibility = showPopup
                ? Android.Views.ImportantForAccessibility.NoHideDescendants
                : Android.Views.ImportantForAccessibility.Auto;

            // Keyboard navigation
            ((ViewGroup)view).DescendantFocusability = showPopup
                ? Android.Views.DescendantFocusability.BlockDescendants
                : Android.Views.DescendantFocusability.AfterDescendants;
            view.ClearFocus();
        }
    }

    public class ParentLayout : ConstraintLayout, Android.Views.ViewTreeObserver.IOnGlobalLayoutListener
    {
        private readonly ViewGroup _decorView;
        private readonly PopupPage _page;
        private readonly ViewGroup _platformView;
        private View _top;
        private View _bottom;
        private View _left;
        private View _right;
        private Android.Graphics.Rect _prevInsets;

        public ParentLayout(Context context, ViewGroup decorView, PopupPage page) : base(context)
        {
            _decorView = decorView;
            _page = page;
            _platformView = (page.Handler!.PlatformView as ViewGroup)!;
            InitContent();

            _page.PropertyChanged += Page_PropertyChanged;

            _decorView.ViewTreeObserver!.AddOnGlobalLayoutListener(this);
        }

        public void RemoveGlobalLayoutListener()
        {
            _page.PropertyChanged -= Page_PropertyChanged;
            _decorView.ViewTreeObserver!.RemoveOnGlobalLayoutListener(this);
        }

        private void InitContent()
        {
            _top = new View(Context) { Id = View.GenerateViewId() };
            _bottom = new View(Context) { Id = View.GenerateViewId() };
            _left = new View(Context) { Id = View.GenerateViewId() };
            _right = new View(Context) { Id = View.GenerateViewId() };
            _platformView.Id = View.GenerateViewId();

            var color = _page.BackgroundColor.ToPlatform();

            _top.SetBackgroundColor(color);
            _bottom.SetBackgroundColor(color);
            _left.SetBackgroundColor(color);
            _right.SetBackgroundColor(color);

            var alpha = (float)_page.Opacity;

            _top.Alpha = alpha;
            _bottom.Alpha = alpha;
            _left.Alpha = alpha;
            _right.Alpha = alpha;

            Android.Graphics.Rect insets;
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var ins = _decorView.RootWindowInsets!.GetInsetsIgnoringVisibility(Android.Views.WindowInsets.Type.SystemBars());
                insets = new Android.Graphics.Rect(ins.Left, ins.Top, ins.Right, ins.Bottom);
            }
            else
            {
                var ins = _decorView.RootWindowInsets!;
                insets = new Android.Graphics.Rect(ins.StableInsetLeft, ins.StableInsetTop, ins.StableInsetRight, ins.StableInsetBottom);
            }

            _prevInsets = insets;

            var topParams = new LayoutParams(LayoutParams.MatchParent, insets.Top);
            _top.LayoutParameters = topParams;

            var bottomParams = new LayoutParams(LayoutParams.MatchParent, insets.Bottom);
            _bottom.LayoutParameters = bottomParams;

            var leftParams = new LayoutParams(insets.Left, LayoutParams.MatchConstraint);
            _left.LayoutParameters = leftParams;

            var rightParams = new LayoutParams(insets.Right, LayoutParams.MatchConstraint);
            _right.LayoutParameters = rightParams;

            var centerParams = new LayoutParams(LayoutParams.MatchConstraint, LayoutParams.MatchConstraint);
            _platformView.LayoutParameters = centerParams;

            this.AddView(_top);
            this.AddView(_bottom);
            this.AddView(_left);
            this.AddView(_right);
            this.AddView(_platformView);

            var set = new ConstraintSet();
            set.Clone(this);

            set.Connect(_top.Id, ConstraintSet.Top, ConstraintSet.ParentId, ConstraintSet.Top);

            set.Connect(_bottom.Id, ConstraintSet.Bottom, ConstraintSet.ParentId, ConstraintSet.Bottom);

            set.Connect(_left.Id, ConstraintSet.Left, ConstraintSet.ParentId, ConstraintSet.Left);
            set.Connect(_left.Id, ConstraintSet.Top, _top.Id, ConstraintSet.Bottom);
            set.Connect(_left.Id, ConstraintSet.Bottom, _bottom.Id, ConstraintSet.Top);

            set.Connect(_right.Id, ConstraintSet.Right, ConstraintSet.ParentId, ConstraintSet.Right);
            set.Connect(_right.Id, ConstraintSet.Top, _top.Id, ConstraintSet.Bottom);
            set.Connect(_right.Id, ConstraintSet.Bottom, _bottom.Id, ConstraintSet.Top);

            set.Connect(_platformView.Id, ConstraintSet.Left, _left.Id, ConstraintSet.Right);
            set.Connect(_platformView.Id, ConstraintSet.Top, _top.Id, ConstraintSet.Bottom);
            set.Connect(_platformView.Id, ConstraintSet.Bottom, _bottom.Id, ConstraintSet.Top);
            set.Connect(_platformView.Id, ConstraintSet.Right, _right.Id, ConstraintSet.Left);

            set.ApplyTo(this);
        }

        private void Page_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == Page.BackgroundColorProperty.PropertyName)
            {
                var color = _page.BackgroundColor.ToPlatform();

                _top.SetBackgroundColor(color);
                _bottom.SetBackgroundColor(color);
                _left.SetBackgroundColor(color);
                _right.SetBackgroundColor(color);
            }
            else if (e.PropertyName == Page.OpacityProperty.PropertyName)
            {
                var alpha = (float)_page.Opacity;

                _top.Alpha = alpha;
                _bottom.Alpha = alpha;
                _left.Alpha = alpha;
                _right.Alpha = alpha;
            }
        }

        public void OnGlobalLayout()
        {
            Android.Graphics.Rect insets;
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var ins = _decorView.RootWindowInsets!.GetInsetsIgnoringVisibility(Android.Views.WindowInsets.Type.SystemBars());
                insets = new Android.Graphics.Rect(ins.Left, ins.Top, ins.Right, ins.Bottom);
            }
            else
            {
                var ins = _decorView.RootWindowInsets!;
                insets = new Android.Graphics.Rect(ins.StableInsetLeft, ins.StableInsetTop, ins.StableInsetRight, ins.StableInsetBottom);
            }

            if (_prevInsets.Top == insets.Top && _prevInsets.Bottom == insets.Bottom
                && _prevInsets.Left == insets.Left && _prevInsets.Right == insets.Right) return;

            _prevInsets = insets;

            var topParams = _top.LayoutParameters!;
            topParams.Height = insets.Top;
            _top.LayoutParameters = topParams;

            var bottomParams = _bottom.LayoutParameters!;
            bottomParams.Height = insets.Bottom;
            _bottom.LayoutParameters = bottomParams;

            var leftParams = _left.LayoutParameters!;
            leftParams.Width = insets.Left;
            _left.LayoutParameters = leftParams;

            var rightParams = _right.LayoutParameters!;
            rightParams.Width = insets.Right;
            _right.LayoutParameters = rightParams;
        }
    }
}