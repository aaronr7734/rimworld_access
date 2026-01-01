# Trade Module

## Purpose
Trading interface navigation.

## Files
**Patches:** TradeNavigationPatch.cs
**States:** TradeNavigationState.cs, TradeConfirmationState.cs

## Key Shortcuts
- **Arrow Keys** - Navigate trade items
- **Tab** - Switch between "Yours" and "Theirs"
- **Enter** - Focus item for quantity adjustment
- **+/-** - Adjust quantity
- **B** - Announce trade balance
- **Space** - Accept trade

## Architecture
Category tabs (Yours, Theirs). Enter to focus item, +/- to adjust, Escape to unfocus. Trade confirmation checks balance.

## Dependencies
**Requires:** ScreenReader/, Input/

## Testing
- [ ] Trade window navigable
- [ ] Quantity adjustment works
- [ ] Trade balance announced
- [ ] Accept trade functional
