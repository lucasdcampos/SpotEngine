using System.Runtime.InteropServices.JavaScript;
using Spot.Core;

namespace Spot.Browser;

/// <summary>
/// The browser <see cref="ICursorController"/>: maps <see cref="Input.CursorLocked"/> onto the DOM Pointer
/// Lock API. Locking hides the cursor and switches pointer events to relative motion (<c>movementX/Y</c>),
/// which <see cref="BrowserHost"/> feeds back as a moving virtual cursor position so mouse-look works exactly
/// as it does on desktop. Because browsers only grant pointer lock from a user gesture, an attempt made
/// outside one is ignored and re-tried on the next canvas click (see the host's pointerdown wiring).
/// </summary>
internal sealed partial class BrowserCursorController : ICursorController
{
    private const string Module = "spot-input";

    /// <inheritdoc />
    public bool Locked
    {
        get => JsIsCursorLocked();
        set => JsSetCursorLocked(value);
    }

    [JSImport("input.setCursorLocked", Module)]
    private static partial void JsSetCursorLocked(bool locked);

    [JSImport("input.isCursorLocked", Module)]
    private static partial bool JsIsCursorLocked();
}
