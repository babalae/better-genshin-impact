import os
import re
from datetime import datetime
from collections import defaultdict
from flask import Flask, jsonify, request, send_from_directory
from dotenv import load_dotenv
import pandas as pd
# 加载环境变量
load_dotenv()

# 获取日志目录路径
BGI_LOG_DIR = os.path.join(os.getenv('BETTERGI_PATH'), 'log')

# 创建Flask应用实例，设置静态文件夹路径为'static'
app = Flask(__name__, static_folder='static')

# ---------------------之后更新的内容粘贴到这里---------------------

# 需要过滤的物品列表
FORBIDDEN_ITEMS = ['调查', '直接拾取']
item_dataframe = pd.DataFrame(columns=['物品名称', '时间', '日期'])
duration_dataframe = pd.DataFrame(columns=['日期', '持续时间（秒）'])
log_list = None


def format_timedelta(seconds):
    """
    将秒数转换为中文 x小时y分钟 格式

    Args:
        seconds: 秒数，可以是整数或None

    Returns:
        str: 格式化后的时间字符串，如 "5小时30分钟"
    """
    if seconds is None:
        return "0分钟"

    # 计算小时和分钟
    hours, remainder = divmod(int(seconds), 3600)
    minutes, _ = divmod(remainder, 60)

    # 拼接字符串（忽略零值部分）
    parts = []
    if hours > 0:
        parts.append(f"{hours}小时")
    if minutes > 0:
        parts.append(f"{minutes}分钟")

    return ''.join(parts) if parts else "0分钟"


def parse_log(log_content, date_str):
    """
    解析日志内容，提取日志类型、交互物品等信息，并统计相关信息。
    支持多次主窗体实例化/退出，自动计算所有段的总时长。

    Args:
        log_content: 日志文件内容
        date_str: 日期字符串

    Returns:
        dict: 包含解析结果的字典
    """
    global item_dataframe, duration_dataframe
    log_pattern = r'\[([^]]+)\] \[([^]]+)\] ([^\n]+)\n?([^\n[]*)'
    matches = re.findall(log_pattern, log_content)

    # type_count = {}
    # interaction_items = []
    item_count = {}
    duration = 0
    cache_dict = {
        '物品名称': [],
        '时间': [],
        '日期': []
    }

    current_start = None  # 当前段开始时间
    current_end = None

    for match in matches:
        timestamp = match[0]  # 时间戳
        level = match[1]  # 日志级别
        log_type = match[2]  # 类名
        details = match[3].strip()  # 日志内容文本

        # 过滤禁用的关键词
        if any(keyword in details for keyword in FORBIDDEN_ITEMS):
            continue

        # 转换时间戳
        current_time = datetime.strptime(timestamp, '%H:%M:%S.%f')

        # 类型统计
        # type_count[log_type] = type_count.get(log_type, 0) + 1

        # 提取拾取内容
        if '交互或拾取' in details:
            item = details.split('：')[1].strip('"')
            # interaction_items.append(item)
            item_count[item] = item_count.get(item, 0) + 1

            # 检查是否存在匹配的行
            existing_row = item_dataframe[
                (item_dataframe['物品名称'] == item) &
                (item_dataframe['时间'] == timestamp) &
                (item_dataframe['日期'] == date_str)
                ]

            # 如果不存在匹配的行，则添加新行
            if existing_row.empty:
                cache_dict['物品名称'].append(item)
                cache_dict['时间'].append(timestamp)
                cache_dict['日期'].append(date_str)

        # 处理时间段
        if not current_start:
            # 开始新的时间段
            current_start = current_time
            current_end = current_time
        else:
            # 计算与上一个有效时间的间隔
            delta = (current_time - current_end).total_seconds()
            if delta <= 300:
                # 表明是连续的事件，更新结束时间
                current_end = current_time
            else:
                # 表明是一段新的事件
                if delta <= 0:
                    logger.critical(
                        f"时间段错误,请检查。有关参数：{timestamp, details, date_str, current_start, current_end, delta}")
                else:
                    # 累加持续时间
                    duration += int(delta)
                # 开始新的时间段
                current_start = current_time
                current_end = current_time

    # 处理最后一段时间
    if current_start and current_end and current_start != current_end:
        delta = (current_end - current_start).total_seconds()
        duration += int(delta)

    return {
        # 'type_count': type_count,
        # 'interaction_items': interaction_items,
        # 'interaction_count': len(interaction_items),
        'item_count': item_count,
        # 'delta_time': format_timedelta(duration),
        'duration': duration,
        'cache_dict': cache_dict
    }


def read_log_file(file_path, date_str):
    """
    读取指定路径的日志文件并解析内容。

    Args:
        file_path: 日志文件路径
        date_str: 日期字符串

    Returns:
        dict: 解析后的日志信息字典，若发生错误则返回错误信息
    """
    try:
        with open(file_path, 'r', encoding='utf-8') as file:
            log_content = file.read()
        return parse_log(log_content, date_str)
    except FileNotFoundError:
        return {"error": "文件未找到"}
    except Exception as e:
        return {"error": f"发生了一个未知错误: {e}"}


def get_log_list():
    """
    获取日志文件列表，并过滤掉不包含交互物品的日志文件。

    Returns:
        list: 过滤后的日志文件名列表
    """
    # 获取所有以'better-genshin-impact'开头的日志文件，并提取日期部分
    log_files = [f.replace('better-genshin-impact', '').replace('.log', '')
                 for f in os.listdir(BGI_LOG_DIR)
                 if f.startswith('better-genshin-impact')]

    filtered_logs = []
    duration_dict = {
        '日期': [],
        '持续时间（秒）': []
    }
    cached_dict = {
        '物品名称': [],
        '时间': [],
        '日期': []
    }
    for file in log_files:
        file_path = os.path.join(BGI_LOG_DIR, f"better-genshin-impact{file}.log")
        result = read_log_file(file_path, file)

        if "error" in result:
            continue

        # 过滤掉不需要的物品
        items = result['item_count'].copy()
        for forbidden_item in FORBIDDEN_ITEMS:
            if forbidden_item in items:
                del items[forbidden_item]

        # 只保留有物品的日志
        if items:
            filtered_logs.append(file)
            duration_dict['日期'].append(file)
            duration_dict['持续时间（秒）'].append(result['duration'])
            cached_dict['物品名称'].extend(result['cache_dict']['物品名称'])
            cached_dict['时间'].extend(result['cache_dict']['时间'])
            cached_dict['日期'].extend(result['cache_dict']['日期'])
    global duration_dataframe, item_dataframe
    duration_dataframe = pd.DataFrame(duration_dict)
    item_dataframe = pd.DataFrame(cached_dict)
    return filtered_logs


# 路由定义
@app.route('/')
def serve_index():
    """
    提供静态资源的路由，返回index.html文件。
    """
    return send_from_directory('static', 'index.html')


@app.route('/<path:filename>')
def serve_static(filename):
    """
    提供静态资源的路由，返回指定的静态文件。
    """
    return send_from_directory('static', filename)


@app.route('/api/LogList', methods=['GET'])
def get_log_list_api():
    """
    提供日志文件列表的API接口。

    Returns:
        JSON: 包含日志文件列表的JSON响应.例如：{'list': ['20250501']}
    """
    global log_list
    if not log_list:
        log_list = get_log_list()
    log_list.reverse()  # 最新的日志排在前面
    return jsonify({'list': log_list})


@app.route('/api/analyse', methods=['GET'])
def analyse_log():
    """
    提供日志分析的API接口，返回指定日期的日志分析结果。
    请求参数:date='all'
    如果没有all，则返回单个日期的数据。
    Returns:
        JSON: 包含日志分析结果的JSON响应。例如：{
        'duration': string,
        'item_count': {item_name:int}
    }
    """
    date = request.args.get('date', 'all')

    if date == 'all':
        return analyse_all_logs()
    else:
        return analyse_single_log(date)


@app.route('/api/item-trend', methods=['GET'])
def item_trend():
    """
    返回单个物品的历史记录。

    Returns:
        JSON: 格式：{
        'data': {‘date':int}
    }
    """
    item_name = request.args.get('item', '')
    if item_name:
        return analyse_item_history(item_name)
    return jsonify({})


@app.route('/api/duration-trend', methods=['GET'])
def duration_trend():
    """
    返回所有日志中，每天的BGI持续运行时间。

    Returns:
        JSON: 格式：{
        'data': {‘date':int}
    }
    """
    return analyse_duration_history()


@app.route('/api/total-items-trend', methods=['GET'])
def item_history():
    """
    返回所有日志中，拾取每天拾取总物品的数量。

    Returns:
        JSON: 格式：{
        'data': {‘date':int}
    }
    """
    return analyse_all_items()


def analyse_all_logs():
    """
    分析所有日志文件并汇总结果

    Returns:
        JSON: 包含所有日志分析结果的JSON响应
    """
    if duration_dataframe.empty or item_dataframe.empty:
        return jsonify({'duration': '0分钟', 'item_count': {}})
    total_duration = duration_dataframe['持续时间（秒）'].sum()
    total_item_count = item_dataframe['物品名称'].value_counts().to_dict()
    return jsonify({
        'duration': format_timedelta(total_duration),
        'item_count': total_item_count
    })


def analyse_single_log(date):
    """
    分析单个日志文件

    Args:
        date: 日志日期

    Returns:
        JSON: 包含单个日志分析结果的JSON响应
    """
    # 筛选特定日期的数据
    filtered_item_df = item_dataframe[item_dataframe['日期'] == date]
    filtered_duration_df = duration_dataframe[duration_dataframe['日期'] == date]
    if filtered_duration_df.empty or filtered_item_df.empty:
        return jsonify({'duration': '0分钟', 'item_count': {}})
    total_duration = filtered_duration_df['持续时间（秒）'].sum()
    total_item_count = filtered_item_df['物品名称'].value_counts().to_dict()
    return jsonify({
        'duration': format_timedelta(total_duration),
        'item_count': total_item_count
    })


def analyse_item_history(item_name):
    """
    分析物品历史数据

    Args:
        item_name: 物品名称

    Returns:
        JSON: 包含物品历史数据的JSON响应
    """
    if item_dataframe.empty:
        return jsonify({'msg': 'no data.'})
    filter_dataframe = item_dataframe[item_dataframe['物品名称'] == item_name]
    # 统计每个日期的数量
    data_counts = filter_dataframe['日期'].value_counts().to_dict()
    return jsonify({
        'data': data_counts
    })


def analyse_duration_history():
    if duration_dataframe.empty:
        return jsonify({'msg': 'no data.'})
    # 按日期分组并计算总持续时间（秒）
    total_seconds = duration_dataframe.groupby(duration_dataframe['日期'])['持续时间（秒）'].sum()

    # 转换为小时并保留一位小数
    total_minutes = (total_seconds // 60).astype(int)
    data_counts = total_minutes.to_dict()
    return jsonify({
        'data': data_counts
    })


def analyse_all_items():
    if item_dataframe.empty:
        return jsonify({'msg': 'no data.'})
    data_counts = item_dataframe['日期'].value_counts().to_dict()
    return jsonify({
        'data': data_counts
    })


# 启动Flask应用
if __name__ == "__main__":
    # import time
    #
    # t1 = time.time()
    # log_list = get_log_list()
    # with app.app_context():
    #     analyse_duration_history()
    #     t2 = time.time()
    #     print(t2 - t1, 's')

    # log_list = get_log_list()

    app.run(debug=False, host='0.0.0.0', port=3000, use_reloader=False)
