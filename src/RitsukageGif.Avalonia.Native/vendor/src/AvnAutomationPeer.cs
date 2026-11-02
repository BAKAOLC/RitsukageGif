using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Automation.Peers;
using Avalonia.Native.Interop;

namespace Avalonia.Native
{
    internal class AvnAutomationPeer : NativeCallbackBase, IAvnAutomationPeer
    {
        private static readonly Dictionary<AutomationProperty, AvnAutomationProperty> SPropertyMap = new()
        {
            { AutomationElementIdentifiers.AutomationIdProperty, AvnAutomationProperty.AutomationPeer_AutomationId },
            {
                AutomationElementIdentifiers.BoundingRectangleProperty,
                AvnAutomationProperty.AutomationPeer_BoundingRectangle
            },
            { AutomationElementIdentifiers.ClassNameProperty, AvnAutomationProperty.AutomationPeer_ClassName },
            { AutomationElementIdentifiers.NameProperty, AvnAutomationProperty.AutomationPeer_Name },
            { RangeValuePatternIdentifiers.ValueProperty, AvnAutomationProperty.RangeValueProvider_Value },
            { ValuePatternIdentifiers.ValueProperty, AvnAutomationProperty.ValueProvider_Value },
            { TogglePatternIdentifiers.ToggleStateProperty, AvnAutomationProperty.ToggleProvider_ToggleState },
            {
                ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                AvnAutomationProperty.ExpandCollapseProvider_ExpandCollapseState
            },
            {
                SelectionItemPatternIdentifiers.IsSelectedProperty,
                AvnAutomationProperty.SelectionItemProvider_IsSelected
            },
            { SelectionPatternIdentifiers.SelectionProperty, AvnAutomationProperty.SelectionProvider_Selection },
        };

        private static readonly ConditionalWeakTable<AutomationPeer, AvnAutomationPeer> SWrappers = new();
        private readonly AutomationPeer _inner;

        private AvnAutomationPeer(AutomationPeer inner)
        {
            _inner = inner;
            _inner.ChildrenChanged += (_, _) => Node?.ChildrenChanged();
            _inner.PropertyChanged += OnPeerPropertyChanged;
            if (inner is IRootProvider root)
                root.FocusChanged += (_, _) => Node?.FocusChanged();
        }

        public IAvnAutomationNode? Node { get; private set; }
        public IAvnString? AcceleratorKey => _inner.GetAcceleratorKey().ToAvnString();
        public IAvnString? AccessKey => _inner.GetAccessKey().ToAvnString();

        public AvnAutomationControlType AutomationControlType =>
            (AvnAutomationControlType)_inner.GetAutomationControlType();

        public IAvnString? AutomationId => _inner.GetAutomationId().ToAvnString();
        public AvnRect BoundingRectangle => _inner.GetBoundingRectangle().ToAvnRect();
        public IAvnAutomationPeerArray Children => new AvnAutomationPeerArray(_inner.GetChildren());
        public IAvnString ClassName => _inner.GetClassName().ToAvnString();
        public IAvnAutomationPeer? LabeledBy => Wrap(_inner.GetLabeledBy());
        public IAvnString Name => _inner.GetName().ToAvnString();
        public IAvnString HelpText => _inner.GetHelpText().ToAvnString();
        public IAvnString PlaceholderText => _inner.GetPlaceholderText().ToAvnString();

        public AvnLandmarkType LandmarkType =>
            (AvnLandmarkType?)_inner.GetLandmarkType() ?? AvnLandmarkType.LandmarkNone;

        public int HeadingLevel => _inner.GetHeadingLevel();
        public IAvnAutomationPeer? Parent => Wrap(_inner.GetParent());

        public IAvnAutomationPeer? TemplatedParent =>
            _inner is ControlAutomationPeer { Owner.TemplatedParent: Control templatedParent }
                ? Wrap(ControlAutomationPeer.CreatePeerForElement(templatedParent))
                : null;

        public IAvnAutomationPeer? VisualRoot => Wrap(_inner.GetAutomationRoot());
        public AvnLiveSetting LiveSetting => (AvnLiveSetting)_inner.GetLiveSetting();

        public IAvnAutomationPeer RootPeer
        {
            get
            {
                var peer = _inner;
                var parent = peer.GetParent();

                while (peer.GetProvider<IRootProvider>() is null && parent is not null)
                {
                    peer = parent;
                    parent = peer.GetParent();
                }

                return Wrap(peer);
            }
        }

        private IEmbeddedRootProvider EmbeddedRootProvider => GetProvider<IEmbeddedRootProvider>();
        private IExpandCollapseProvider ExpandCollapseProvider => GetProvider<IExpandCollapseProvider>();
        private IInvokeProvider InvokeProvider => GetProvider<IInvokeProvider>();
        private IRangeValueProvider RangeValueProvider => GetProvider<IRangeValueProvider>();
        private IRootProvider RootProvider => GetProvider<IRootProvider>();
        private ISelectionItemProvider SelectionItemProvider => GetProvider<ISelectionItemProvider>();
        private IToggleProvider ToggleProvider => GetProvider<IToggleProvider>();
        private IValueProvider ValueProvider => GetProvider<IValueProvider>();

        public int HasKeyboardFocus()
        {
            return _inner.HasKeyboardFocus().AsComBool();
        }

        public int IsContentElement()
        {
            return _inner.IsContentElement().AsComBool();
        }

        public int IsControlElement()
        {
            return _inner.IsControlElement().AsComBool();
        }

        public int IsEnabled()
        {
            return _inner.IsEnabled().AsComBool();
        }

        public int IsKeyboardFocusable()
        {
            return _inner.IsKeyboardFocusable().AsComBool();
        }

        public void SetFocus()
        {
            _inner.SetFocus();
        }

        public int ShowContextMenu()
        {
            return _inner.ShowContextMenu().AsComBool();
        }

        public void BringIntoView()
        {
            _inner.BringIntoView();
        }

        public void SetNode(IAvnAutomationNode node)
        {
            if (Node is not null)
                throw new InvalidOperationException("The AvnAutomationPeer already has a node.");
            Node = node;
        }

        public int IsInteropPeer()
        {
            return (_inner is InteropAutomationPeer).AsComBool();
        }

        public IntPtr InteropPeer_GetNativeControlHandle()
        {
            return ((InteropAutomationPeer)_inner).NativeControlHandle.Handle;
        }

        public int IsRootProvider()
        {
            return IsProvider<IRootProvider>();
        }

        public IAvnWindowBase? RootProvider_GetWindow()
        {
            return (RootProvider.PlatformImpl as WindowBaseImpl)?.Native;
        }

        public IAvnAutomationPeer? RootProvider_GetFocus()
        {
            return Wrap(RootProvider.GetFocus());
        }

        public IAvnAutomationPeer? RootProvider_GetPeerFromPoint(AvnPoint point)
        {
            var result = RootProvider.GetPeerFromPoint(point.ToAvaloniaPoint());

            if (result is null)
                return null;

            // The OSX accessibility APIs expect non-ignored elements when hit-testing.
            while (!result.IsControlElement())
            {
                var parent = result.GetParent();

                if (parent is not null)
                    result = parent;
                else
                    break;
            }

            return Wrap(result);
        }


        public int IsEmbeddedRootProvider()
        {
            return IsProvider<IEmbeddedRootProvider>();
        }

        public IAvnAutomationPeer? EmbeddedRootProvider_GetFocus()
        {
            return Wrap(EmbeddedRootProvider.GetFocus());
        }

        public IAvnAutomationPeer? EmbeddedRootProvider_GetPeerFromPoint(AvnPoint point)
        {
            var result = EmbeddedRootProvider.GetPeerFromPoint(point.ToAvaloniaPoint());

            if (result is null)
                return null;

            // The OSX accessibility APIs expect non-ignored elements when hit-testing.
            while (!result.IsControlElement())
            {
                var parent = result.GetParent();

                if (parent is not null)
                    result = parent;
                else
                    break;
            }

            return Wrap(result);
        }

        public int IsExpandCollapseProvider()
        {
            return IsProvider<IExpandCollapseProvider>();
        }

        public int ExpandCollapseProvider_GetIsExpanded()
        {
            return ExpandCollapseProvider.ExpandCollapseState switch
            {
                ExpandCollapseState.Expanded => 1,
                ExpandCollapseState.PartiallyExpanded => 1,
                _ => 0,
            };
        }

        public int ExpandCollapseProvider_GetShowsMenu()
        {
            return ExpandCollapseProvider.ShowsMenu.AsComBool();
        }

        public void ExpandCollapseProvider_Expand()
        {
            ExpandCollapseProvider.Expand();
        }

        public void ExpandCollapseProvider_Collapse()
        {
            ExpandCollapseProvider.Collapse();
        }

        public int IsInvokeProvider()
        {
            return IsProvider<IInvokeProvider>();
        }

        public void InvokeProvider_Invoke()
        {
            InvokeProvider.Invoke();
        }

        public int IsRangeValueProvider()
        {
            return IsProvider<IRangeValueProvider>();
        }

        public double RangeValueProvider_GetValue()
        {
            return RangeValueProvider.Value;
        }

        public double RangeValueProvider_GetMinimum()
        {
            return RangeValueProvider.Minimum;
        }

        public double RangeValueProvider_GetMaximum()
        {
            return RangeValueProvider.Maximum;
        }

        public double RangeValueProvider_GetSmallChange()
        {
            return RangeValueProvider.SmallChange;
        }

        public double RangeValueProvider_GetLargeChange()
        {
            return RangeValueProvider.LargeChange;
        }

        public void RangeValueProvider_SetValue(double value)
        {
            RangeValueProvider.SetValue(value);
        }

        public int RangeValueProvider_IsReadOnly()
        {
            return RangeValueProvider.IsReadOnly.AsComBool();
        }

        public int IsSelectionItemProvider()
        {
            return IsProvider<ISelectionItemProvider>();
        }

        public int SelectionItemProvider_IsSelected()
        {
            return SelectionItemProvider.IsSelected.AsComBool();
        }

        public void SelectionItemProvider_Select()
        {
            SelectionItemProvider.Select();
        }

        public void SelectionItemProvider_AddToSelection()
        {
            SelectionItemProvider.AddToSelection();
        }

        public void SelectionItemProvider_RemoveFromSelection()
        {
            SelectionItemProvider.RemoveFromSelection();
        }

        public IAvnAutomationPeer? ScrollProvider_GetHorizontalScrollBar()
        {
            return _inner is ScrollViewerAutomationPeer scrollViewer
                ? Wrap(scrollViewer.GetHorizontalScrollBarPeer())
                : null;
        }

        public IAvnAutomationPeer? ScrollProvider_GetVerticalScrollBar()
        {
            return _inner is ScrollViewerAutomationPeer scrollViewer
                ? Wrap(scrollViewer.GetVerticalScrollBarPeer())
                : null;
        }

        public int IsToggleProvider()
        {
            return IsProvider<IToggleProvider>();
        }

        public int ToggleProvider_GetToggleState()
        {
            return (int)ToggleProvider.ToggleState;
        }

        public void ToggleProvider_Toggle()
        {
            ToggleProvider.Toggle();
        }

        public int IsValueProvider()
        {
            return IsProvider<IValueProvider>();
        }

        public IAvnString? ValueProvider_GetValue()
        {
            return ValueProvider.Value.ToAvnString();
        }

        public void ValueProvider_SetValue(string value)
        {
            ValueProvider.SetValue(value);
        }

        public int ValueProvider_IsReadOnly()
        {
            return ValueProvider.IsReadOnly.AsComBool();
        }

        ~AvnAutomationPeer()
        {
            Node?.Dispose();
        }

        [return: NotNullIfNotNull("peer")]
        public static AvnAutomationPeer? Wrap(AutomationPeer? peer)
        {
            return peer is null ? null : SWrappers.GetValue(peer, _ => new(peer));
        }

        private T GetProvider<T>()
        {
            return _inner.GetProvider<T>() ?? throw new InvalidOperationException(
                $"The peer {_inner} does not implement {typeof(T)}.");
        }

        private int IsProvider<T>()
        {
            return (_inner.GetProvider<T>() is not null).AsComBool();
        }

        private void OnPeerPropertyChanged(object? sender, AutomationPropertyChangedEventArgs e)
        {
            if (SPropertyMap.TryGetValue(e.Property, out var property))
                Node?.PropertyChanged(property);
        }
    }

    internal class AvnAutomationPeerArray : NativeCallbackBase, IAvnAutomationPeerArray
    {
        private readonly AvnAutomationPeer[] _items;

        public AvnAutomationPeerArray(IReadOnlyList<AutomationPeer> items)
        {
            _items = [.. items.Select(x => AvnAutomationPeer.Wrap(x))];
        }

        public uint Count => (uint)_items.Length;

        public IAvnAutomationPeer Get(uint index)
        {
            return _items[index];
        }
    }
}
