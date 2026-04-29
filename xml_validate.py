#!/usr/bin/env python3
import xml.etree.ElementTree as ET
import sys

files = [
    r'C:\Users\felipe.quintella\Dev\netrisk\src\GUIClient\Styles\WindowStyles.axaml',
    r'C:\Users\felipe.quintella\Dev\netrisk\src\GUIClient\Views\NavigationBar.axaml',
    r'C:\Users\felipe.quintella\Dev\netrisk\src\GUIClient\Views\VulnerabilitiesView.axaml',
    r'C:\Users\felipe.quintella\Dev\netrisk\src\GUIClient\Views\UsersView.axaml',
    r'C:\Users\felipe.quintella\Dev\netrisk\src\GUIClient\Views\ReportsWindow.axaml',
    r'C:\Users\felipe.quintella\Dev\netrisk\src\GUIClient\Views\RiskView.axaml'
]

print("\n" + "="*70)
print("XML VALIDATION REPORT - AXAML FILES")
print("="*70 + "\n")

all_valid = True
for filepath in files:
    filename = filepath.split('\\')[-1]
    try:
        ET.parse(filepath)
        print(f"✓ {filename:35} VALID XML")
    except ET.ParseError as e:
        all_valid = False
        print(f"✗ {filename:35} INVALID XML")
        print(f"  Error: {str(e)}\n")
    except Exception as e:
        all_valid = False
        print(f"✗ {filename:35} ERROR")
        print(f"  Error: {str(e)}\n")

print("="*70)
if all_valid:
    print("Result: ALL FILES VALID ✓")
else:
    print("Result: SOME FILES HAVE ERRORS ✗")
print("="*70 + "\n")
