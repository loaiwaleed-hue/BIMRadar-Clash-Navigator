# Verification: #96 Clear Overrides ribbon button

Scope: code read only. Revit untested. Covers reads (API existence, logic review) and NOT writes: the override reset must be proven by the owner on a model copy.

## Revit API (2022-2027)
View.GetElementOverrides, View.SetElementOverrides, View.SetCategoryOverrides, OverrideGraphicSettings (Halftone, Transparency, *LineColor, Surface/Cut foreground and background pattern colours), Color.IsValid exist in Revit 2022-2027. No ElementId.IntegerValue used; no version difference, so nothing added to Compat.cs.

## Read against the AICon routine
- Plottable check, isOverridden test and blank OverrideGraphicSettings reset are copied 1:1 from reference-source/clear-view-overrides/Routine.cs.
- Active view only; no view list, no deep scan (elements visible in the view via FilteredElementCollector(doc, view.Id)).
- One Transaction "Clear Overrides" (rolled back when nothing changed).
- Clash Viewer: NavHandler.ResetTrackedCategories(view) (new, inside the same transaction) resets the tracked category overrides of that view with a blank setting and removes the #83 tracking. If the view was tracked, ColorsOn = false and NavForm.SyncColors() (try/catch) refreshes the button to "Create override".
- Messages: "Cleared N element override(s) in <view>.", "No overrides in <view>.", not-a-plottable-view text.

## Not verified here
Compilation (no .NET), ribbon button in pyRevit and standalone, the transaction and Ctrl+Z behaviour, and the Clash Viewer toggle sync.
