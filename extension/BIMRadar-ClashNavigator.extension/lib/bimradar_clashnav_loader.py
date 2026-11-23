# -*- coding: utf-8 -*-
"""Shared loader for the BIMRadar Clash Viewer pyRevit buttons.

IronPython 2.7 compatible. Usage in a button script:

    from bimradar_clashnav_loader import launcher
    launcher(__revit__).Open(__revit__)
"""
import os
import clr

_loaded = {}


class LoaderError(Exception):
    pass


def _extension_root():
    root = os.path.dirname(os.path.abspath(__file__))
    while root and not root.lower().endswith('.extension'):
        parent = os.path.dirname(root)
        if parent == root:
            raise LoaderError("Could not locate the .extension folder from " + os.path.abspath(__file__))
        root = parent
    return root


def _tfm(version):
    if version <= 2024:
        return 'net48'
    if version <= 2026:
        return 'net8'
    return 'net10'


def launcher(uiapp):
    """Return the BIMRadar.ClashNavigator.Launcher class for the running Revit.

    uiapp is the __revit__ object passed in by the calling script.
    Raises LoaderError if the DLL is missing.
    """
    version = int(uiapp.Application.VersionNumber)
    tfm = _tfm(version)
    dll = os.path.join(_extension_root(), 'bin', tfm, 'BIMRadar.ClashNavigator.dll')
    if not os.path.exists(dll):
        raise LoaderError(
            "BIMRadar.ClashNavigator.dll not found for Revit " + str(version) + ":\n" + dll +
            "\n\nUse the pyRevit zip built by CI (it contains bin/" + tfm + ").")
    if dll not in _loaded:
        clr.AddReferenceToFileAndPath(dll)
        _loaded[dll] = True
    from BIMRadar.ClashNavigator import Launcher
    return Launcher
