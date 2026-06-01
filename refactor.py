import re

with open('ModbusForge/Views/VisualNodeEditor.xaml', 'r', encoding='utf-8') as f:
    content = f.read()

# Add xmlns if missing
if 'xmlns:avalonDock=' not in content:
    content = content.replace('xmlns:sys=', 'xmlns:avalonDock=\"https://github.com/Dirkster99/AvalonDock\"\n             xmlns:sys=')

# We'll extract the 4 panels using regex
pou_tree_match = re.search(r'<!-- Left Panel: POU Tree -->\s*(<Border Grid\.Column="0".*?)(?=\s*<!-- Splitter -->)', content, re.DOTALL)
canvas_match = re.search(r'<!-- Canvas Area -->\s*(<Border Grid\.Column="2".*?)(?=\s*<!-- Splitter -->)', content, re.DOTALL)
palette_match = re.search(r'<!-- Right Panel: Node Palette -->\s*(<Border Grid\.Column="4".*?)(?=\s*<!-- Splitter -->)', content, re.DOTALL)
controls_match = re.search(r'<!-- Simulation Controls Panel -->\s*(<Border Grid\.Column="6".*?</Border>)', content, re.DOTALL)

if not (pou_tree_match and canvas_match and palette_match and controls_match):
    print("Could not match panels")
    exit(1)

pou_tree = pou_tree_match.group(1).replace('Grid.Column="0" ', '')
canvas = canvas_match.group(1).replace('Grid.Column="2" ', '')
palette = palette_match.group(1).replace('Grid.Column="4" ', '')
controls = controls_match.group(1).replace('Grid.Column="6" ', '')

new_layout = f'''    <avalonDock:DockingManager x:Name="SimDockingManager">
        <avalonDock:LayoutRoot>
            <avalonDock:LayoutPanel Orientation="Horizontal">
                <avalonDock:LayoutAnchorablePane DockWidth="200">
                    <avalonDock:LayoutAnchorable Title="Programs" CanClose="False">
{pou_tree}
                    </avalonDock:LayoutAnchorable>
                </avalonDock:LayoutAnchorablePane>
                
                <avalonDock:LayoutDocumentPane>
                    <avalonDock:LayoutDocument Title="Canvas" CanClose="False">
{canvas}
                    </avalonDock:LayoutDocument>
                </avalonDock:LayoutDocumentPane>

                <avalonDock:LayoutAnchorablePane DockWidth="200">
                    <avalonDock:LayoutAnchorable Title="Palette" CanClose="False">
{palette}
                    </avalonDock:LayoutAnchorable>
                </avalonDock:LayoutAnchorablePane>

                <avalonDock:LayoutAnchorablePane DockWidth="250">
                    <avalonDock:LayoutAnchorable Title="Controls" CanClose="False">
{controls}
                    </avalonDock:LayoutAnchorable>
                </avalonDock:LayoutAnchorablePane>
            </avalonDock:LayoutPanel>
        </avalonDock:LayoutRoot>
    </avalonDock:DockingManager>'''

# Replace the outer grid
content = re.sub(r'<Grid>\s*<Grid\.ColumnDefinitions>.*?</Grid>', new_layout, content, flags=re.DOTALL)

with open('ModbusForge/Views/VisualNodeEditor.xaml', 'w', encoding='utf-8') as f:
    f.write(content)

print("Refactored successfully")
