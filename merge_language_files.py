#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
合并语言文件脚本
将额外的通知消息和日志消息文件合并到主语言文件中
"""

import json
import os
from pathlib import Path

def load_json_file(file_path):
    """加载JSON文件"""
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            return json.load(f)
    except Exception as e:
        print(f"加载文件 {file_path} 失败: {e}")
        return None

def save_json_file(file_path, data):
    """保存JSON文件"""
    try:
        with open(file_path, 'w', encoding='utf-8') as f:
            json.dump(data, f, ensure_ascii=False, indent=4)
        return True
    except Exception as e:
        print(f"保存文件 {file_path} 失败: {e}")
        return False

def merge_language_files():
    """合并语言文件"""
    languages_dir = Path("BetterGenshinImpact/Languages")
    
    # 定义文件映射
    file_mappings = [
        {
            'main': 'zh-CN.json',
            'extras': ['notification-messages-zh.json', 'log-messages-zh.json']
        },
        {
            'main': 'en-US.json', 
            'extras': ['notification-messages-en.json', 'log-messages-en.json']
        }
    ]
    
    for mapping in file_mappings:
        main_file = languages_dir / mapping['main']
        
        # 加载主文件
        main_data = load_json_file(main_file)
        if not main_data:
            continue
            
        print(f"处理主文件: {mapping['main']}")
        
        # 合并额外文件
        for extra_file_name in mapping['extras']:
            extra_file = languages_dir / extra_file_name
            if not extra_file.exists():
                print(f"  额外文件不存在: {extra_file_name}")
                continue
                
            extra_data = load_json_file(extra_file)
            if not extra_data:
                continue
                
            print(f"  合并文件: {extra_file_name}")
            
            # 合并到主文件的strings部分
            if 'strings' in main_data:
                main_data['strings'].update(extra_data)
            else:
                # 如果主文件没有strings结构，直接合并
                main_data.update(extra_data)
        
        # 保存合并后的主文件
        if save_json_file(main_file, main_data):
            print(f"  成功保存合并后的文件: {mapping['main']}")
        else:
            print(f"  保存失败: {mapping['main']}")
    
    # 删除额外的文件
    files_to_delete = [
        'notification-messages-zh.json',
        'notification-messages-en.json', 
        'log-messages-zh.json',
        'log-messages-en.json'
    ]
    
    print("\n删除额外的语言文件:")
    for file_name in files_to_delete:
        file_path = languages_dir / file_name
        if file_path.exists():
            try:
                file_path.unlink()
                print(f"  已删除: {file_name}")
            except Exception as e:
                print(f"  删除失败 {file_name}: {e}")
        else:
            print(f"  文件不存在: {file_name}")

if __name__ == "__main__":
    merge_language_files()
    print("\n语言文件合并完成！")