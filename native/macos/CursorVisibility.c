#include "CursorVisibility.h"
#include <CoreGraphics/CoreGraphics.h>

bool rgc_cursor_is_visible(void)
{
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
    return CGCursorIsVisible() != 0;
#pragma clang diagnostic pop
}
