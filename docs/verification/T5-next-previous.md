# T5 verification: Launcher.Next / Launcher.Previous

Scope: this verifies the code path only (by reading the source). It does not verify Revit writes; those are proven in the owner's test (#67 / T9).

## Call path (Next)

1. Ribbon button (pyRevit or add-in) calls `Launcher.Next(UIApplication)` in `Launcher.cs`.
2. `Launcher.Next` calls `Step(true)`. If `NavForm.Instance` is null or `IsDisposed`, it shows the TaskDialog "Open Clash Navigator first." and returns (no exception).
3. Otherwise `Step` calls `NavForm.GoNext()` inside try/catch (errors shown via `ex.Message`).
4. `NavForm.GoNext()` is `Go("Next", 0)`. This is the same call the window's `btnNext.Click` handler and the Right / PageDown keys make.
5. `NavForm.Go` runs `EnsureFilter()`, sets `H.Action`, `H.GoTo`, `H.UseBox`, `H.BoxM`, then calls `Ev.Raise()`.
6. Revit calls `NavHandler.Execute(UIApplication)` (the `IExternalEventHandler`), which reads `Action` ("Next") and moves to the next position in `Order`.

Previous is identical, using `GoPrevious()`, `Go("Prev", 0)` and the "Prev" branch in `NavHandler.Execute`.

## Same path as the window buttons

`btnNext.Click` and `btnPrev.Click` call `Go("Next", 0)` and `Go("Prev", 0)`. `GoNext` and `GoPrevious` call those same lines, so the ribbon and the buttons share one path. This is the only change to `NavForm`: two public wrapper methods.

## Threading

- `Go` reads WinForms controls (`chk.Checked`, `num.Value`) and, through `EnsureFilter`/`CurrentKey`/`ApplyFilter`, may read or update controls. It must therefore run on the UI thread that owns the form.
- Ribbon commands run on the Revit main thread. The modeless form is created on that same thread (it is opened from a Revit command), so calling `GoNext` from the ribbon is a same-thread call. `ExternalEvent.Raise` is also valid in an API context.
- Not verified here: that the form is always created on the Revit main thread in every launch path. This is assumed from `NavigatorEntry.Open` being called from `Launcher.Open`.

## Not verified

- No compile was possible in this sandbox (no .NET, no NuGet). CI must build it.
- No Revit run: the actual view change and any writes are covered by #67 / T9.
