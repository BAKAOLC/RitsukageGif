using Avalonia.Controls;

namespace RitsukageGif.Desktop
{
    // ReSharper disable once ClassCannotBeInstantiated
    internal sealed partial class PinnedImageWindow
    {
        private bool _transformCommandsInitialized;

        private void EnsureTransformCommands()
        {
            if (_transformCommandsInitialized || _content.ContextMenu is not { } menu) return;
            _transformCommandsInitialized = true;
            var transforms = CreateImageProcessingMenu();
            var insertion = menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.Tag as string == "copy");
            var index = insertion is null ? 0 : menu.Items.IndexOf(insertion);
            menu.Items.Insert(index, transforms);
            menu.Items.Insert(index + 1, CreateTransparentLayerMenu(menu));
        }

        private bool CanTransformImage(PinnedImageTransform transform)
        {
            return !_closed && !_busy && _annotation is null && !IsMouseThrough &&
                   CanTransformLiveImage() &&
                   (transform is PinnedImageTransform.Grayscale or PinnedImageTransform.Invert ||
                    !IsOperationLocked(PinnedLockRestrictions.Move));
        }

        private async Task ApplyImageTransformAsync(PinnedImageTransform transform, CancellationToken token = default)
        {
            if (!Enum.IsDefined(transform)) throw new ArgumentOutOfRangeException(nameof(transform));
            token.ThrowIfCancellationRequested();
            if (_closed || _annotation is not null || IsMouseThrough ||
                (transform is not (PinnedImageTransform.Grayscale or PinnedImageTransform.Invert) &&
                 IsOperationLocked(PinnedLockRestrictions.Move))) return;
            using var work = token.CanBeCanceled && token != _lifetime.Token
                ? CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, token)
                : null;
            var workToken = work?.Token ?? _lifetime.Token;
            if (transform is not (PinnedImageTransform.Grayscale or PinnedImageTransform.Invert))
            {
                if (!CanTransformLiveImage()) return;
                ApplyViewTransform(transform);
                return;
            }

            if (!CanTransformLiveImage()) return;
            var next = transform == PinnedImageTransform.Grayscale
                ? _colorMatrixState.ToggleGrayscale()
                : _colorMatrixState.Invert();
            await ApplyImageProcessingAsync(next, workToken);
        }

        private static string TransformAction(PinnedImageTransform transform)
        {
            return transform switch
            {
                PinnedImageTransform.RotateClockwise => "rotate-clockwise",
                PinnedImageTransform.RotateCounterclockwise => "rotate-counterclockwise",
                PinnedImageTransform.FlipHorizontal => "flip-horizontal",
                PinnedImageTransform.FlipVertical => "flip-vertical",
                PinnedImageTransform.Grayscale => "grayscale", PinnedImageTransform.Invert => "invert",
                _ => throw new ArgumentOutOfRangeException(nameof(transform)),
            };
        }

        private static PinnedImageTransform? ParseTransformAction(string? action)
        {
            return action switch
            {
                "rotate-clockwise" => PinnedImageTransform.RotateClockwise,
                "rotate-counterclockwise" => PinnedImageTransform.RotateCounterclockwise,
                "flip-horizontal" => PinnedImageTransform.FlipHorizontal,
                "flip-vertical" => PinnedImageTransform.FlipVertical,
                "grayscale" => PinnedImageTransform.Grayscale, "invert" => PinnedImageTransform.Invert, _ => null,
            };
        }

        private static string TransformLabel(PinnedImageTransform transform)
        {
            return transform switch
            {
                PinnedImageTransform.RotateClockwise => "PinnedImageRotateClockwise",
                PinnedImageTransform.RotateCounterclockwise => "PinnedImageRotateCounterclockwise",
                PinnedImageTransform.FlipHorizontal => "AnimationFlipHorizontal",
                PinnedImageTransform.FlipVertical => "AnimationFlipVertical",
                PinnedImageTransform.Grayscale => "AnimationGrayscale",
                PinnedImageTransform.Invert => "AnimationInvert",
                _ => throw new ArgumentOutOfRangeException(nameof(transform)),
            };
        }
    }
}
