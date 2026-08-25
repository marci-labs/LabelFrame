# 生成中文许可 RTF（\uNNNN 转义，MSI RichEdit 兼容）；产物 packaging/license.rtf
import io

def esc(text: str) -> str:
    out = []
    for ch in text:
        o = ord(ch)
        if ch in '\\{}':
            out.append('\\' + ch)
        elif o < 128:
            out.append(ch)
        else:
            if o > 32767:
                o -= 65536
            out.append('\\u%d?' % o)
    return ''.join(out)

paras = [
    ('b', 'LabelFrame 软件使用许可'),
    ('', ''),
    ('', '感谢您安装 LabelFrame。本软件为仓库场景的标签打印解决方案，包含 LabelFrame Client（打印客户端）与 LabelFrame Server（无头服务端）。在安装或使用本软件前，请仔细阅读以下条款：'),
    ('', '一、本软件按「现状」提供，不附带任何明示或默示的担保，使用者需自行承担打印内容与业务数据的使用责任。'),
    ('', '二、允许在本组织内部部署、使用与复制；未经授权不得将本软件用于商业再分发。'),
    ('', '三、本软件安装与运行产生的配置、模板与日志数据存储于本机，请妥善管理与备份。'),
    ('', '四、LabelFrame 保留对本许可条款的最终解释权。'),
    ('', ''),
    ('', '(c) 2026 LabelFrame'),
]

body = []
for style, text in paras:
    if text == '':
        body.append('\\par')
    else:
        prefix = '\\b ' if style == 'b' else ''
        suffix = '\\b0' if style == 'b' else ''
        body.append(prefix + esc(text) + suffix + '\\par')

rtf = ('{\\rtf1\\ansi\\ansicpg936\\deff0{\\fonttbl{\\f0\\fnil\\fcharset134 ' + esc('微软雅黑') + ';}}\n'
       '\\viewkind4\\uc1\\pard\\lang2052\\f0\\fs18\n' + '\n'.join(body) + '\n}')
io.open('packaging/license.rtf', 'w', encoding='ascii', newline='\n').write(rtf)
print('packaging/license.rtf written,', len(rtf), 'chars')
