# -*- coding: utf-8 -*-
from pyrevit import forms

try:
    from bimradar_clashnav_loader import launcher
    launcher(__revit__).ClearOverrides(__revit__)
except Exception as e:
    forms.alert(str(e), title='BIMRadar')
