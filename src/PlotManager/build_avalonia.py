import subprocess

def run_cmd(args):
    print(f"Running: {' '.join(args)}")
    res = subprocess.run(args, cwd=r"C:\Projects\AOGChemModule\AOG-Plot-Manager\src\PlotManager", capture_output=True, text=True)
    print(res.stdout)
    if res.stderr:
        print("ERROR:", res.stderr)

run_cmd(["dotnet", "new", "avalonia.app", "-o", "PlotManager.Avalonia", "-n", "PlotManager.Avalonia", "-f", "net8.0"])
run_cmd(["dotnet", "sln", "PlotManager.sln", "add", r"PlotManager.Avalonia\PlotManager.Avalonia.csproj"])
run_cmd(["dotnet", "add", r"PlotManager.Avalonia\PlotManager.Avalonia.csproj", "reference", r"PlotManager.Core\PlotManager.Core.csproj"])
run_cmd(["dotnet", "add", r"PlotManager.Avalonia\PlotManager.Avalonia.csproj", "package", "Mapsui.Avalonia"])
