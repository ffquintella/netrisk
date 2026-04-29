import xml.etree.ElementTree as ET

files = [
    r'C:\Users\felipe.quintella\Dev\netrisk\src\GUIClient\Styles\WindowStyles.axaml',
    r'C:\Users\felipe.quintella\Dev\netrisk\src\GUIClient\Views\NavigationBar.axaml',
    r'C:\Users\felipe.quintella\Dev\netrisk\src\GUIClient\Views\VulnerabilitiesView.axaml',
    r'C:\Users\felipe.quintella\Dev\netrisk\src\GUIClient\Views\UsersView.axaml',
    r'C:\Users\felipe.quintella\Dev\netrisk\src\GUIClient\Views\ReportsWindow.axaml',
    r'C:\Users\felipe.quintella\Dev\netrisk\src\GUIClient\Views\RiskView.axaml'
]

for f in files:
    try:
        ET.parse(f)
        fname = f.split('\\')[-1]
        print(f'✓ {fname} - Valid XML')
    except Exception as e:
        fname = f.split('\\')[-1]
        print(f'✗ {fname} - ERROR: {str(e)}')
