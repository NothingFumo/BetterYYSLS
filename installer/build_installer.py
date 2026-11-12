"""构建 BetterYYSLS 的 Windows MSI 安装包。

前置条件：
  - .NET 10 SDK
  - WiX Toolset 5：dotnet tool install --global wix --version 5.0.2

用法：
  python installer/build_installer.py
输出：
  dist/BetterYYSLS-Setup.msi
"""
from __future__ import annotations

import shutil
import subprocess
import sys
import uuid
from pathlib import Path
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "BetterYYSLS" / "BetterYYSLS.csproj"
PUBLISH_DIR = ROOT / "dist" / "publish"
DIST_DIR = ROOT / "dist"
WXS_PATH = DIST_DIR / "installer.wxs"
MSI_PATH = DIST_DIR / "BetterYYSLS-Setup.msi"
APP_ICON = ROOT / "BetterYYSLS" / "Assets" / "logo.ico"

# 固定 GUID，保证升级安装可以识别同一产品。
UPGRADE_CODE = "A4A74D8E-3D63-4B34-9A6B-7A0C8F8C2D11"
GUID_NAMESPACE = uuid.UUID("c5f3e6b8-8d5f-4f2f-a0d5-8a7b7c1f6d10")


def run(cmd: list[str]) -> None:
    print(">", " ".join(cmd), flush=True)
    subprocess.run(cmd, cwd=ROOT, check=True)


def stable_id(prefix: str, rel: str) -> str:
    return prefix + uuid.uuid5(GUID_NAMESPACE, rel).hex[:24]


def stable_guid(rel: str) -> str:
    return str(uuid.uuid5(GUID_NAMESPACE, "component:" + rel)).upper()


def xml_escape(value: str) -> str:
    return escape(value, {'"': "&quot;"})


def collect_files() -> list[Path]:
    return sorted(
        path for path in PUBLISH_DIR.rglob("*")
        if path.is_file()
    )


def build_directory_tree(files: list[Path]) -> tuple[str, list[str]]:
    """生成嵌套 Directory 和每个目录的 Component，返回 XML 片段和组件 ID。"""
    by_dir: dict[Path, list[Path]] = {}
    for file in files:
        rel = file.relative_to(PUBLISH_DIR)
        by_dir.setdefault(rel.parent, []).append(file)

    all_dirs: set[Path] = {Path(".")}
    for directory in by_dir:
        current = directory
        while current != Path("."):
            all_dirs.add(current)
            current = current.parent

    directory_ids = {
        directory: ("INSTALLFOLDER" if directory == Path(".") else stable_id("d_", directory.as_posix()))
        for directory in all_dirs
    }

    component_ids: list[str] = []

    def directory_xml(directory: Path) -> str:
        children = sorted(
            (d for d in all_dirs if d.parent == directory and d != Path(".")),
            key=lambda d: d.name.lower(),
        )
        lines: list[str] = []
        for child in children:
            lines.append(
                f'        <Directory Id="{directory_ids[child]}" Name="{xml_escape(child.name)}">'
            )
            lines.append(directory_xml(child))
            lines.append("        </Directory>")
        for file in sorted(by_dir.get(directory, []), key=lambda p: p.name.lower()):
            rel = file.relative_to(PUBLISH_DIR).as_posix()
            cid = stable_id("c_", rel)
            fid = stable_id("f_", rel)
            component_ids.append(cid)
            lines.append(f'        <Component Id="{cid}" Guid="{stable_guid(rel)}">')
            lines.append(
                f'            <File Id="{fid}" Source="{xml_escape(str(file))}" />'
            )
            lines.append("        </Component>")
        return "\n".join(lines)

    body = directory_xml(Path("."))
    return body, component_ids


def main() -> int:
    if shutil.which("wix") is None:
        print("未找到 wix，请先执行：dotnet tool install --global wix --version 5.0.2", file=sys.stderr)
        return 1

    DIST_DIR.mkdir(parents=True, exist_ok=True)
    skip_publish = "--skip-publish" in sys.argv
    if not skip_publish:
        if PUBLISH_DIR.exists():
            shutil.rmtree(PUBLISH_DIR)
        run([
            "dotnet", "publish", str(PROJECT),
            "-c", "Release",
            "-r", "win-x64",
            "--self-contained", "true",
            "-p:PublishSingleFile=false",
            "-p:DebugType=None",
            "-p:DebugSymbols=false",
            "-o", str(PUBLISH_DIR),
        ])

    files = collect_files()
    if not files:
        print("发布目录为空", file=sys.stderr)
        return 1

    directory_body, component_ids = build_directory_tree(files)
    component_refs = "\n".join(
        f'            <ComponentRef Id="{cid}" />' for cid in component_ids
    )

    wxs = f'''<?xml version="1.0" encoding="utf-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
    <Package Name="BetterYYSLS"
             Manufacturer="BetterYYSLS"
             Version="1.0.0"
             UpgradeCode="{UPGRADE_CODE}"
             Scope="perMachine"
             Compressed="yes"
             InstallerVersion="500">
        <MajorUpgrade DowngradeErrorMessage="已安装更新版本的 BetterYYSLS。" />
        <MediaTemplate EmbedCab="yes" CompressionLevel="high" />

        <Icon Id="AppIcon" SourceFile="{xml_escape(str(APP_ICON))}" />
        <Property Id="ARPPRODUCTICON" Value="AppIcon" />
        <Property Id="ARPHELPLINK" Value="https://github.com/NothingFumo/BetterYYSLS" />

        <StandardDirectory Id="ProgramFiles64Folder">
            <Directory Id="INSTALLFOLDER" Name="BetterYYSLS" />
        </StandardDirectory>

        <StandardDirectory Id="ProgramMenuFolder">
            <Directory Id="ApplicationProgramsFolder" Name="BetterYYSLS" />
        </StandardDirectory>

        <StandardDirectory Id="DesktopFolder" />

        <Feature Id="MainFeature" Title="BetterYYSLS" Level="1">
{component_refs}
            <ComponentRef Id="ApplicationShortcut" />
            <ComponentRef Id="DesktopShortcut" />
        </Feature>
    </Package>

    <Fragment>
        <DirectoryRef Id="INSTALLFOLDER">
{directory_body}
        </DirectoryRef>
    </Fragment>

    <Fragment>
        <DirectoryRef Id="ApplicationProgramsFolder">
            <Component Id="ApplicationShortcut" Guid="7C1D2F3E-4A5B-4C6D-8E9F-0A1B2C3D4E5F">
                <Shortcut Id="ApplicationStartMenuShortcut"
                          Name="BetterYYSLS"
                          Description="BetterYYSLS 游戏自动化工具"
                          Target="[INSTALLFOLDER]BetterYYSLS.exe"
                          WorkingDirectory="INSTALLFOLDER" />
                <RemoveFolder Id="RemoveApplicationProgramsFolder" On="uninstall" />
                <RegistryValue Root="HKCU" Key="Software\\BetterYYSLS" Name="installed"
                               Type="integer" Value="1" KeyPath="yes" />
            </Component>
        </DirectoryRef>
    </Fragment>

    <Fragment>
        <DirectoryRef Id="DesktopFolder">
            <Component Id="DesktopShortcut" Guid="8D2E3F4A-5B6C-4D7E-9F0A-1B2C3D4E5F60">
                <Shortcut Id="ApplicationDesktopShortcut"
                          Name="BetterYYSLS"
                          Description="BetterYYSLS 游戏自动化工具"
                          Target="[INSTALLFOLDER]BetterYYSLS.exe"
                          WorkingDirectory="INSTALLFOLDER" />
                <RegistryValue Root="HKCU" Key="Software\\BetterYYSLS" Name="desktopShortcut"
                               Type="integer" Value="1" KeyPath="yes" />
            </Component>
        </DirectoryRef>
    </Fragment>
</Wix>
'''
    WXS_PATH.write_text(wxs, encoding="utf-8")

    if MSI_PATH.exists():
        MSI_PATH.unlink()
    run([
        "wix", "build", str(WXS_PATH),
        "-arch", "x64",
        "-o", str(MSI_PATH),
    ])
    print(f"安装包已生成：{MSI_PATH}", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
