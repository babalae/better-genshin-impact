#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Better Genshin Impact Build Trigger Script
用于触发CNB构建的Python脚本
"""

import requests
import json
import sys
import argparse

def trigger_build(token, branch="main", event="api_trigger_one", runid=None):
    """
    触发构建请求
    
    Args:
        token (str): 授权token
        branch (str): 分支名称，默认为main
        event (str): 事件类型，默认为api_trigger_one
        runid (str): 运行ID，可选参数
    
    Returns:
        dict: API响应结果
    """
    url = "https://api.cnb.cool/bettergi/better-genshin-impact/-/build/start"
    
    headers = {
        "Accept": "application/json",
        "Authorization": f"Bearer {token}",
        "Content-Type": "application/json",
        "Host": "api.cnb.cool",
        "Connection": "keep-alive"
    }
    
    data = {
        "branch": branch,
        "event": event
    }
    
    # 如果提供了runid，则添加到env中
    if runid:
        data["env"] = {
            "RUN_ID": runid
        }
    
    try:
        print(f"正在发起构建请求...")
        print(f"URL: {url}")
        
        print(f"请求体: {json.dumps(data, indent=2, ensure_ascii=False)}")
        
        response = requests.post(url, headers=headers, json=data)
        
        print(f"响应状态码: {response.status_code}")
        
        if response.status_code == 200:
            result = response.json()
            print("构建触发成功!")
            print(f"响应内容: {json.dumps(result, indent=2, ensure_ascii=False)}")
            return result
        else:
            print(f"请求失败: {response.status_code}")
            print(f"错误信息: {response.text}")
            return None
            
    except requests.exceptions.RequestException as e:
        print(f"请求异常: {e}")
        return None
    except json.JSONDecodeError as e:
        print(f"JSON解析错误: {e}")
        print(f"响应内容: {response.text}")
        return None

def main():
    parser = argparse.ArgumentParser(description="触发Better Genshin Impact构建")
    parser.add_argument("token", help="授权token")
    parser.add_argument("--branch", default="main", help="分支名称 (默认: main)")
    parser.add_argument("--event", default="api_trigger_one", help="事件类型 (默认: api_trigger_one)")
    parser.add_argument("--runid", help="运行ID (可选)")
    
    args = parser.parse_args()
    
    if not args.token:
        print("错误: 必须提供token参数")
        sys.exit(1)
    
    result = trigger_build(args.token, args.branch, args.event, args.runid)
    
    if result is None:
        sys.exit(1)

if __name__ == "__main__":
    main()