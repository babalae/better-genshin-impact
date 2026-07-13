#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
GitHub Actions 构建物下载和上传脚本

该脚本用于：
1. 从 GitHub Actions 下载最新的构建物 (BetterGI_7z 和 BetterGI_Install)
2. 解压构建物到本地
3. 调用 cnb_release.py 上传文件到 CNB

使用方法：
    python github_download_and_cnb_upload.py --cnb-token YOUR_CNB_TOKEN [--github-token YOUR_GITHUB_TOKEN] [--run-id RUN_ID]

参数说明：
    --cnb-token: CNB API Token (必需)
    --github-token: GitHub Personal Access Token (可选，用于提高API限制)
    --run-id: 指定 GitHub Actions 运行 ID (可选，默认获取最新运行)

依赖：
- requests: HTTP 请求库
- tqdm: 进度条显示库

安装依赖：pip install -r requirements.txt
"""

import os
import sys
import json
import requests
import zipfile
import tempfile
import shutil
import re
from pathlib import Path
from typing import List, Dict, Optional
from tqdm import tqdm

# 导入 CNBReleaseUploader
from cnb_release import CNBReleaseUploader


class GitHubActionsDownloader:
    def __init__(self, token: Optional[str] = None):
        """
        初始化 GitHub Actions 下载器
        
        Args:
            token: GitHub Personal Access Token (可选，用于提高API限制)
        """
        self.token = token
        self.headers = {
            'Accept': 'application/vnd.github.v3+json',
            'User-Agent': 'BetterGI-Downloader/1.0.0'
        }
        if token:
            self.headers['Authorization'] = f'token {token}'
    
    def get_latest_workflow_run(self, owner: str, repo: str, workflow_file: str) -> Optional[Dict]:
        """
        获取最新的工作流运行
        
        Args:
            owner: 仓库所有者
            repo: 仓库名称
            workflow_file: 工作流文件名
            
        Returns:
            最新的工作流运行信息或None
        """
        url = f'https://api.github.com/repos/{owner}/{repo}/actions/workflows/{workflow_file}/runs'
        params = {
            'status': 'completed',
            'conclusion': 'success',
            'per_page': 1
        }
        
        try:
            response = requests.get(url, headers=self.headers, params=params)
            response.raise_for_status()
            
            data = response.json()
            runs = data.get('workflow_runs', [])
            
            if not runs:
                print("❌ 没有找到成功完成的工作流运行")
                return None
                
            latest_run = runs[0]
            print(f"✅ 找到最新的工作流运行:")
            print(f"   Run ID: {latest_run['id']}")
            print(f"   创建时间: {latest_run['created_at']}")
            print(f"   状态: {latest_run['status']} / {latest_run['conclusion']}")
            print(f"   分支: {latest_run['head_branch']}")
            
            return latest_run
            
        except requests.exceptions.RequestException as e:
            print(f"❌ 获取工作流运行失败: {e}")
            return None
    
    def get_artifacts(self, owner: str, repo: str, run_id: int) -> List[Dict]:
        """
        获取指定运行的构建物列表
        
        Args:
            owner: 仓库所有者
            repo: 仓库名称
            run_id: 运行ID
            
        Returns:
            构建物列表
        """
        url = f'https://api.github.com/repos/{owner}/{repo}/actions/runs/{run_id}/artifacts'
        
        try:
            response = requests.get(url, headers=self.headers)
            response.raise_for_status()
            
            data = response.json()
            artifacts = data.get('artifacts', [])
            
            print(f"📦 找到 {len(artifacts)} 个构建物:")
            for artifact in artifacts:
                print(f"   - {artifact['name']} ({artifact['size_in_bytes']:,} bytes)")
            
            return artifacts
            
        except requests.exceptions.RequestException as e:
            print(f"❌ 获取构建物列表失败: {e}")
            return []
    
    def download_artifact(self, owner: str, repo: str, artifact_id: int, 
                         artifact_name: str, download_dir: str) -> Optional[str]:
        """
        下载指定的构建物
        
        Args:
            owner: 仓库所有者
            repo: 仓库名称
            artifact_id: 构建物ID
            artifact_name: 构建物名称
            download_dir: 下载目录
            
        Returns:
            下载的文件路径或None
        """
        url = f'https://api.github.com/repos/{owner}/{repo}/actions/artifacts/{artifact_id}/zip'
        
        try:
            print(f"📥 开始下载构建物: {artifact_name}")
            response = requests.get(url, headers=self.headers, stream=True)
            response.raise_for_status()
            
            # 获取文件总大小
            total_size = int(response.headers.get('content-length', 0))
            
            # 保存到临时文件
            zip_path = os.path.join(download_dir, f"{artifact_name}.zip")
            
            # 使用 tqdm 创建进度条
            chunk_size = 8192
            with open(zip_path, 'wb') as f:
                with tqdm(
                    total=total_size,
                    unit='B',
                    unit_scale=True,
                    unit_divisor=1024,
                    desc=f"下载 {artifact_name}",
                    ncols=80,
                    bar_format='{desc}: {percentage:3.0f}%|{bar}| {n_fmt}/{total_fmt} [{elapsed}<{remaining}, {rate_fmt}]'
                ) as pbar:
                    for chunk in response.iter_content(chunk_size=chunk_size):
                        if chunk:
                            f.write(chunk)
                            pbar.update(len(chunk))
            
            print(f"✅ 下载完成: {zip_path}")
            return zip_path
            
        except requests.exceptions.RequestException as e:
            print(f"❌ 下载构建物失败 ({artifact_name}): {e}")
            return None
    

    
    def extract_artifact(self, zip_path: str, extract_dir: str) -> List[str]:
        """
        解压构建物
        
        Args:
            zip_path: ZIP文件路径
            extract_dir: 解压目录
            
        Returns:
            解压出的文件列表
        """
        extracted_files = []
        
        try:
            print(f"📂 解压构建物: {os.path.basename(zip_path)}")
            with zipfile.ZipFile(zip_path, 'r') as zip_ref:
                zip_ref.extractall(extract_dir)
                extracted_files = [os.path.join(extract_dir, name) for name in zip_ref.namelist()]
            
            print(f"✅ 解压完成，共 {len(extracted_files)} 个文件")
            for file_path in extracted_files:
                if os.path.isfile(file_path):
                    size = os.path.getsize(file_path)
                    print(f"   - {os.path.basename(file_path)} ({size:,} bytes)")
            
            return extracted_files
            
        except Exception as e:
            print(f"❌ 解压失败: {e}")
            return []


def extract_version_from_filename(filename: str) -> Optional[str]:
    """
    从文件名中提取版本号
    
    Args:
        filename: 文件名
        
    Returns:
        版本号或None
    """
    # 去除扩展名
    import os
    filename_without_ext = os.path.splitext(filename)[0]
    
    # 匹配版本号模式，如 v1.2.3, 1.2.3-alpha.1 等
    patterns = [
        r'v?([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?)',
        r'_v?([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?)',
    ]
    
    for pattern in patterns:
        match = re.search(pattern, filename_without_ext)
        if match:
            return match.group(1)
    
    return None


def create_cnb_config(files: List[str], version: str, token: str) -> Dict:
    """
    创建CNB上传配置
    
    Args:
        files: 要上传的文件列表
        version: 版本号
        token: CNB token
        
    Returns:
        CNB配置字典
    """
    # 判断是否为预发布版本
    is_prerelease = '-' in version
    make_latest = "false" if is_prerelease else "true"
    
    config = {
        "token": token,
        "project_path": "bettergi/better-genshin-impact",
        "base_url": "https://api.cnb.cool",
        "overwrite": True,
        "release_data": {
            "tag_name": f"v{version}",
            "name": f"BetterGI v{version}",
            "body": f"BetterGI v{version} 自动发布",
            "draft": False,
            "prerelease": is_prerelease,
            "target_commitish": "main",
            "make_latest": make_latest
        },
        "asset_files": files
    }
    
    return config


def main():
    import argparse
    
    # 解析命令行参数
    parser = argparse.ArgumentParser(description='BetterGI 构建物下载和上传工具')
    parser.add_argument('--run-id', type=str, help='指定 GitHub Actions 运行 ID，如果提供则不会获取最新运行')
    parser.add_argument('--github-token', type=str, help='GitHub Personal Access Token')
    parser.add_argument('--cnb-token', type=str, required=True, help='CNB API Token (必需)')
    args = parser.parse_args()
    
    print("🚀 BetterGI 构建物下载和上传工具")
    print("=" * 50)
    
    # 获取 token，优先使用命令行参数，其次使用环境变量
    github_token = args.github_token or os.getenv('GITHUB_TOKEN')
    cnb_token = args.cnb_token or os.getenv('CNB_TOKEN')
    
    if not cnb_token:
        print("❌ 错误: 请设置 CNB_TOKEN 环境变量")
        return 1
    
    if not github_token:
        print("⚠️  警告: 未设置 GITHUB_TOKEN，可能会遇到API限制")
    
    # 确定运行 ID
    if args.run_id:
        print(f"\n🎯 使用指定的运行 ID: {args.run_id}")
        run_id = args.run_id
    else:
        # 创建下载器来获取最新运行 ID
        downloader = GitHubActionsDownloader(github_token)
        print("\n🔍 查找最新的工作流运行...")
        latest_run = downloader.get_latest_workflow_run('babalae', 'better-genshin-impact', 'publish.yml')
        if not latest_run:
            return 1
        run_id = str(latest_run['id'])
    
    # 使用当前目录下的固定目录，以action运行ID命名
    work_dir = os.path.join(os.getcwd(), 'github_actions_cache', run_id)
    download_dir = os.path.join(work_dir, 'downloads')
    extract_dir = os.path.join(work_dir, 'extracted')
    
    print(f"\n📁 使用工作目录: {work_dir}")
    
    # 检查是否已存在解压后的文件
    all_files = []
    version = None
    
    # 检查解压目录是否已存在且包含文件
    if os.path.exists(extract_dir):
        print("🔍 检查已存在的构建物...")
        existing_files = []
        # 预期的构建物名称
        expected_artifacts = ['BetterGI_7z', 'BetterGI_Install']
        
        for artifact_name in expected_artifacts:
            artifact_extract_dir = os.path.join(extract_dir, artifact_name)
            if os.path.exists(artifact_extract_dir):
                for root, dirs, files in os.walk(artifact_extract_dir):
                    for file in files:
                        file_path = os.path.join(root, file)
                        existing_files.append(file_path)
                        
                        # 尝试从文件名提取版本号
                        if not version:
                            filename = os.path.basename(file_path)
                            extracted_version = extract_version_from_filename(filename)
                            if extracted_version:
                                version = extracted_version
        
        if existing_files and version:
            print(f"✅ 发现已存在的构建物 ({len(existing_files)} 个文件)，跳过下载")
            print(f"📋 检测到版本号: {version}")
            all_files = existing_files
        else:
            print("⚠️  已存在目录但未找到有效文件，将重新下载")
    
    # 如果没有找到已存在的文件，则进行下载和解压
    if not all_files:
        print("📥 需要下载构建物，正在获取构建物信息...")
        
        # 如果还没有创建下载器，现在创建
        if 'downloader' not in locals():
            downloader = GitHubActionsDownloader(github_token)
        
        # 获取构建物列表
        print("\n📦 获取构建物列表...")
        artifacts = downloader.get_artifacts('babalae', 'better-genshin-impact', int(run_id))
        if not artifacts:
            return 1
        
        # 筛选需要的构建物
        target_artifacts = []
        for artifact in artifacts:
            if artifact['name'] in ['BetterGI_7z', 'BetterGI_Install']:
                target_artifacts.append(artifact)
        
        if len(target_artifacts) != 2:
            print(f"❌ 错误: 期望找到2个构建物，实际找到 {len(target_artifacts)} 个")
            return 1
        
        print("📥 开始下载和解压构建物...")
        os.makedirs(download_dir, exist_ok=True)
        os.makedirs(extract_dir, exist_ok=True)
        
        for artifact in target_artifacts:
            # 下载
            zip_path = downloader.download_artifact(
                'babalae', 'better-genshin-impact', 
                artifact['id'], artifact['name'], download_dir
            )
            
            if not zip_path:
                continue
            
            # 解压
            artifact_extract_dir = os.path.join(extract_dir, artifact['name'])
            os.makedirs(artifact_extract_dir, exist_ok=True)
            
            extracted_files = downloader.extract_artifact(zip_path, artifact_extract_dir)
            
            # 收集文件并提取版本号
            for file_path in extracted_files:
                if os.path.isfile(file_path):
                    all_files.append(file_path)
                    
                    # 尝试从文件名提取版本号
                    if not version:
                        filename = os.path.basename(file_path)
                        extracted_version = extract_version_from_filename(filename)
                        if extracted_version:
                            version = extracted_version
                            print(f"📋 检测到版本号: {version}")
        
    if not all_files:
        print("❌ 错误: 没有找到可上传的文件")
        return 1
    
    if not version:
        print("❌ 错误: 无法从文件名中提取版本号")
        return 1
    
    print(f"\n📋 准备上传 {len(all_files)} 个文件:")
    for file_path in all_files:
        size = os.path.getsize(file_path)
        print(f"   - {os.path.basename(file_path)} ({size:,} bytes)")
    
    # 创建CNB配置
    print("\n⚙️  创建CNB配置...")
    cnb_config = create_cnb_config(all_files, version, cnb_token)
    
    # 保存配置文件
    config_path = os.path.join(work_dir, 'cnb_config.json')
    with open(config_path, 'w', encoding='utf-8') as f:
        json.dump(cnb_config, f, indent=2, ensure_ascii=False)
    
    print(f"✅ 配置文件已保存: {config_path}")
    
    # 直接调用 CNBReleaseUploader
    print("\n🚀 开始上传到CNB...")
    
    try:
        # 创建 CNBReleaseUploader 实例
        uploader = CNBReleaseUploader(
            token=cnb_config['token'],
            base_url=cnb_config.get('base_url', 'https://api.cnb.cool')
        )
        
        # 创建 release
        print(f"📝 创建 release: {cnb_config['release_data']['name']}")
        release_result = uploader.create_release(
            project_path=cnb_config['project_path'],
            release_data=cnb_config['release_data']
        )
        
        if not release_result:
            print("❌ 创建 release 失败")
            return 1
        
        print(f"✅ Release 创建成功: {release_result['name']}")
        
        # 上传文件
        print(f"📤 开始上传 {len(cnb_config['asset_files'])} 个文件...")
        upload_results = uploader.upload_multiple_assets(
            project_path=cnb_config['project_path'],
            release_id=release_result['id'],
            asset_files=cnb_config['asset_files'],
            overwrite=cnb_config.get('overwrite', True)
        )
        
        # 检查上传结果
        success_count = sum(1 for result in upload_results if result)
        total_count = len(upload_results)
        
        print(f"\n📊 上传结果汇总:")
        print(f"   ✅ 成功: {success_count}/{total_count}")
        
        if success_count < total_count:
            print(f"   ❌ 失败: {total_count - success_count}/{total_count}")
            for i, result in enumerate(upload_results):
                if not result:
                    file_name = os.path.basename(cnb_config['asset_files'][i])
                    print(f"      - {file_name}: 上传失败")
        
        if success_count == total_count:
            print("\n🎉 所有文件上传完成!")
            return 0
        else:
            print("\n❌ 部分文件上传失败")
            return 1
            
    except Exception as e:
        print(f"❌ CNB上传失败: {e}")
        return 1


if __name__ == '__main__':
    try:
        exit_code = main()
        sys.exit(exit_code)
    except KeyboardInterrupt:
        print("\n⚠️  用户中断操作")
        sys.exit(1)
    except Exception as e:
        print(f"\n💥 程序异常: {e}")
        sys.exit(1)