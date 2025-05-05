import os.path
import re
from flask import Flask, jsonify, request, send_from_directory
from flask_cors import CORS
from dotenv import load_dotenv

# 加载环境变量
load_dotenv()

# 获取日志目录路径
bgi_logdir = os.path.join(os.getenv('BETTERGI_PATH'), 'log')

# 创建Flask应用实例，设置静态文件夹路径为'static'
app = Flask(__name__, static_folder='static')

# 启用跨域资源共享
CORS(app)


# 解析日志内容的函数
def parse_log(log_content):
    """
    解析日志内容，提取日志类型、交互物品等信息，并统计相关信息。
    :param log_content: 日志内容字符串
    :return: 包含日志类型统计、交互物品列表、交互物品统计等信息的字典
    """
    log_pattern = r'\[([^]]+)\] \[([^]]+)\] ([^\n]+)\n?([^\n[]*)'
    matches = re.findall(log_pattern, log_content)

    type_count = {}
    interaction_items = []

    for match in matches:
        timestamp = match[0]
        level = match[1]
        log_type = match[2]
        details = match[3].strip()

        # 统计每种类型的出现次数
        if log_type in type_count:
            type_count[log_type] += 1
        else:
            type_count[log_type] = 1

        # 提取交互或拾取的物品
        if '交互或拾取' in details:
            item = details.split('：')[1].strip('"')
            interaction_items.append(item)

    # 统计交互或拾取物品中每个字符串出现的次数
    item_count = {}
    for item in interaction_items:
        if item in item_count:
            item_count[item] += 1
        else:
            item_count[item] = 1

    return {
        'type_count': type_count,
        'interaction_items': interaction_items,
        'interaction_count': len(interaction_items),
        'item_count': item_count
    }


# 读取日志文件并解析内容
def read_log_file(file_path):
    """
    读取指定路径的日志文件并解析内容。
    :param file_path: 日志文件路径
    :return: 解析后的日志信息字典，若发生错误则返回错误信息
    """
    try:
        with open(file_path, 'r', encoding='utf-8') as file:
            log_content = file.read()
        return parse_log(log_content)
    except FileNotFoundError:
        return {"error": "文件未找到"}
    except Exception as e:
        return {"error": f"发生了一个未知错误: {e}"}


# 获取日志文件列表
def get_log_list():
    """
    获取日志文件列表，并过滤掉不包含交互物品的日志文件。
    :return: 过滤后的日志文件名列表
    """
    l = [f.replace('better-genshin-impact', '').replace('.log', '') for f in os.listdir(bgi_logdir) if
         f.startswith('better-genshin-impact')]
    l2 = []
    for file in l:
        file_path = os.path.join(bgi_logdir, f"better-genshin-impact{file}.log")
        result = read_log_file(file_path)
        if "error" in result:
            continue
        items = result['item_count']
        if '调查' in items:
            del items['调查']
        if len(items) == 0:
            continue
        l2.append(file)
    return l2


# 获取日志文件列表
log_list = get_log_list()
log_list.reverse()


# 提供静态资源的路由
@app.route('/')
def serve_index():
    """
    提供静态资源的路由，返回index.html文件。
    """
    return send_from_directory('static', 'index.html')


# 提供静态资源的路由
@app.route('/<path:filename>')
def serve_static(filename):
    """
    提供静态资源的路由，返回指定的静态文件。
    """
    return send_from_directory('static', filename)


# 提供日志文件列表的API接口
@app.route('/api/LogList', methods=['GET'])
def get_log_list_api():
    """
    提供日志文件列表的API接口。
    :return: 包含日志文件列表的JSON响应
    """
    global log_list
    return jsonify({'list': log_list})


# 提供日志分析的API接口
@app.route('/api/analyse', methods=['GET'])
def analyse_log():
    """
    提供日志分析的API接口，返回指定日期的日志分析结果。
    :return: 包含日志分析结果的JSON响应
    """
    date = request.args.get('date', '20250430')
    file_path = os.path.join(bgi_logdir, f"better-genshin-impact{date}.log")
    result = read_log_file(file_path)

    if "error" in result:
        return jsonify(result), 400
    items = result['item_count']
    if '调查' in items:
        del items['调查']
    response = {
        'item_count': items
    }
    return jsonify(response)


# 启动Flask应用
if __name__ == "__main__":
    app.run(debug=True, host='0.0.0.0', port=5000)
