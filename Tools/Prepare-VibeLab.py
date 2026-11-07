"""Stage a portable Gradle submission from a successfully built Unity Release export.

Run RoomSiegeVibeLabBuild.ExportAndroid and the exported :launcher:assembleRelease
first. This tool never invokes IL2CPP or substitutes libraries from another build.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import struct
import subprocess


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--export", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    source = args.export.resolve()
    output = args.output.resolve()
    project = Path(__file__).resolve().parent.parent
    if output.exists():
        raise SystemExit("Output already exists; choose a new empty destination.")
    apk = source / "launcher/build/outputs/apk/release/launcher-release.apk"
    if not apk.is_file():
        raise SystemExit("Build the exported :launcher:assembleRelease successfully first.")
    runtime = source / "unityLibrary/src/main/jniLibs/arm64-v8a/libil2cpp.so"
    metadata = source / "unityLibrary/src/main/assets/bin/Data/Managed/Metadata/global-metadata.dat"
    if not runtime.is_file() or not metadata.is_file():
        raise SystemExit("Missing the compiled IL2CPP runtime or matching metadata.")
    boot = source / "unityLibrary/src/main/assets/bin/Data/boot.config"
    if "player-connection-ip=" in boot.read_text(encoding="utf-8"):
        raise SystemExit("Export a non-development player before staging.")
    commit = subprocess.check_output(
        ["git", "-C", str(project), "rev-parse", "HEAD"], text=True
    ).strip()
    # Copy build inputs, excluding native compiler tools, intermediates and caches.
    ignored = shutil.ignore_patterns(
        "build", ".gradle", ".cxx", "Il2CppOutputProject", "jniStaticLibs", "symbols"
    )
    output.mkdir(parents=True)
    for folder in ("launcher", "unityLibrary", "shared"):
        shutil.copytree(source / folder, output / folder, ignore=ignored)
    shutil.copy2(source / "settings.gradle", output / "settings.gradle")
    settings = output / "settings.gradle"
    settings.write_text(settings.read_text(encoding="utf-8") +
                        '\nrootProject.name = "RoomSiege-VibeLab"\n', encoding="utf-8")
    for module in ("launcher", "unityLibrary"):
        gradle = output / module / "build.gradle"
        text = gradle.read_text(encoding="utf-8")
        if module == "unityLibrary":
            marker = "tasks.register('buildIl2Cpp')"
            if marker not in text:
                raise SystemExit("Unrecognized Unity template: expected buildIl2Cpp.")
            text = text.split(marker)[0]
            text += "\nandroid.sourceSets.main.jni.srcDirs = []\n"
        else:
            text = text.replace("apply from: 'setupSymbols.gradle'\n", "")
        text = re.sub(r'^\s*(ndkPath|ndkVersion)\s+.*\n', '', text, flags=re.M)
        # Native binaries were compiled by Unity's matching NDK. Preserve them exactly,
        # so cloud packaging needs the Android SDK but no host-specific NDK/IL2CPP.
        text += "\nandroid.packaging.jniLibs.keepDebugSymbols += ['**/*.so']\n"
        gradle.write_text(text, encoding="utf-8")
    properties = (source / "gradle.properties").read_text(encoding="utf-8")
    remove_keys = {
        "unityProjectPath", "unity.projectPath", "unity.androidSdkPath",
        "unity.androidNdkPath", "unity.jdkPath",
        "android.builtInKotlin", "android.r8.strictFullModeForKeepRules",
    }
    lines = [line for line in properties.splitlines()
             if line.split("=", 1)[0] not in remove_keys]
    properties = "\n".join(lines).replace("unity.agpVersion=9.1.1", "unity.agpVersion=8.10.1")
    (output / "gradle.properties").write_text(properties + "\n", encoding="utf-8")
    (output / "build.gradle").write_text('''plugins {
    id 'com.android.application' version '8.10.1' apply false
    id 'com.android.library' version '8.10.1' apply false
}

// Fail clearly if a checkout is missing genuine Unity runtime inputs.
tasks.register('verifyUnityRuntime') {
    doLast {
        def provenance = new groovy.json.JsonSlurper().parse(file('build-provenance.json'))
        provenance.critical_files.each { entry ->
            def input = file(entry.path)
            if (!input.isFile()) throw new GradleException("Missing Unity runtime: ${entry.path}")
            def digest = java.security.MessageDigest.getInstance('SHA-256')
            input.withInputStream { stream ->
                byte[] buffer = new byte[65536]
                int count
                while ((count = stream.read(buffer)) != -1) digest.update(buffer, 0, count)
            }
            if (digest.digest().encodeHex().toString() != entry.sha256)
                throw new GradleException("Unity runtime checksum mismatch: ${entry.path}")
        }
    }
}
subprojects {
    tasks.matching { it.name == 'preBuild' }.configureEach {
        dependsOn rootProject.tasks.named('verifyUnityRuntime')
    }
}
tasks.register('clean', Delete) { delete rootProject.layout.buildDirectory }
''', encoding="utf-8")
    (output / ".gitignore").write_text(
        ".gradle/\n**/build/\nlocal.properties\n*.apk\n*.aab\n*.jks\n*.keystore\n.cxx/\n",
        encoding="utf-8"
    )
    (output / ".gitattributes").write_text(
        "* text=auto\n*.so binary\n*.aar binary\n*.jar binary\n*.png binary\n",
        encoding="utf-8"
    )
    critical = []
    for path in sorted((output / "unityLibrary/src/main/jniLibs").rglob("*.so")):
        header = path.read_bytes()[:20]
        if header[:5] != b"\x7fELF\x02" or struct.unpack_from("<H", header, 18)[0] != 183:
            raise SystemExit(f"Expected a real ARM64 ELF64 library: {path}")
        critical.append({"path": path.relative_to(output).as_posix(), "sha256": sha256(path)})
    for relative in (
        "unityLibrary/src/main/assets/bin/Data/Managed/Metadata/global-metadata.dat",
        "launcher/src/main/res/mipmap-mdpi/app_icon.png",
    ):
        path = output / relative
        critical.append({"path": relative, "sha256": sha256(path)})
    # Check every copied asset, including the compiled scene and configuration data.
    for path in (source / "unityLibrary/src/main/assets").rglob("*"):
        if path.is_file() and sha256(path) != sha256(output / path.relative_to(source)):
            raise SystemExit(f"Asset copy mismatch: {path.relative_to(source)}")
    provenance = {
        "schema": 1, "source_repository": "https://github.com/ultimetamr/RoomSiege",
        "source_commit": commit, "unity_version": "6000.6.8f1",
        "unity_configuration": "Release", "gradle_version": "8.11.1",
        "android_gradle_plugin": "8.10.1", "application_id": "com.roomsiege.mr",
        "unity_export_apk_sha256": sha256(apk), "critical_files": critical,
    }
    (output / "build-provenance.json").write_text(
        json.dumps(provenance, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    media = output / "Docs/Media"
    media.mkdir(parents=True)
    shutil.copy2(project / "Docs/Media/RoomSiege-Icon-1024.png", media)
    print(f"Staged portable Gradle build: {output}")
    print(f"Source commit: {commit}; verified {len(critical)} critical runtime inputs.")


if __name__ == "__main__":
    main()
