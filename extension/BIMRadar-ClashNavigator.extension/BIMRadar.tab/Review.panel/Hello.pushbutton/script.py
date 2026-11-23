# -*- coding: utf-8 -*-
__title__ = "Hello"
__doc__ = "Shows which Revit and .NET runtime the BIMRadar Clash Navigator loaded on."
import clr, os

v = int(__revit__.Application.VersionNumber)
tfm = 'net48' if v <= 2024 else ('net8' if v <= 2026 else 'net10')

root = os.path.dirname(os.path.abspath(__file__))
while root and not root.lower().endswith('.extension'):
    parent = os.path.dirname(root)
    if parent == root:
        break
    root = parent

dll = os.path.join(root, 'bin', tfm, 'BIMRadar.ClashNavigator.dll')
if not os.path.exists(dll):
    from pyrevit import forms
    forms.alert("BIMRadar.ClashNavigator.dll not found for Revit " + str(v) + ":\n" + dll +
                "\n\nUse the pyRevit zip built by CI (it contains bin/" + tfm + ").", title="BIMRadar")
else:
    clr.AddReferenceToFileAndPath(dll)
    from BIMRadar.ClashNavigator import Launcher
    Launcher.Hello(__revit__)
