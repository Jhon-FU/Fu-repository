"""Rebuild the five bundled banks deterministically from checked-in source data.

Requires Python 3.10+, standard library only. Never reads a user's progress.
"""
from pathlib import Path
import json, itertools, hashlib

ROOT=Path(__file__).resolve().parents[1]
DATA=ROOT/'data'
def read(name):return json.loads((DATA/'sources'/name).read_text('utf-8'))
lexicon={x['word']:x for x in read('lexicon.json')}
exclusions=read('school-exclusions.json')
school=set(exclusions['schoolWords'])|set(exclusions['basicDerivatives'])
frequency=read('exam-frequency.json')
four={w for w,x in lexicon.items() if 'cet4' in x['tags']}
six={w for w,x in lexicon.items() if 'cet6' in x['tags']}
pure4=four-school
pure6=six-four-school
def evidence(word,kind):return frequency[kind].get(word,{'months':[],'papers':[],'occurrences':0})
def high(word,kind):
 e=evidence(word,kind)
 return len(e['months'])>=3 and len({m[:4] for m in e['months']})>=2 and e['occurrences']>=5
def ordered(words,kind):
 return sorted(words,key=lambda w:(-len(evidence(w,kind)['months']),-evidence(w,kind)['occurrences'],w))
hi4=ordered({w for w in pure4 if high(w,'cet4')},'cet4')
hi6=ordered({w for w in pure6 if high(w,'cet6')},'cet6')
merged=[w for pair in itertools.zip_longest(hi4,hi6) for w in pair if w is not None]
definitions=[
 ('cet4-pure','纯四级词库',ordered(pure4,'cet4'),'四级词表减高中及基础词形。词表范围以所附 ECDICT 标签为准，不是官方完整大纲。'),
 ('cet4-high','四级高频词库',hi4,'四级增量词中，在四级阅读样本至少出现于 3 个考期、跨 2 年、累计 5 次。已排除高中及基础词形。'),
 ('cet6-pure','纯六级词库',ordered(pure6,'cet6'),'六级词表减四级、高中及基础词形后的增量词。词表范围以所附 ECDICT 标签为准。'),
 ('cet6-high','六级高频词库',hi6,'六级增量词中，在六级阅读样本至少出现于 3 个考期、跨 2 年、累计 5 次。'),
 ('cet46-high','四六级高频词库（不含高中）',merged,'四级与六级高频增量词交替排列并去重；每类高频使用对应考试阅读样本，已排除高中及基础词形。'),
]
out=DATA/'recommended';out.mkdir(parents=True,exist_ok=True)
summary=[]
for identity,name,words,description in definitions:
 assert words and len(words)==len(set(words)) and not (set(words)&school)
 cards=[]
 for rank,word in enumerate(words,1):
  source=lexicon[word];kind='cet4' if word in pure4 else 'cet6';ev=evidence(word,kind)
  label='四级' if kind=='cet4' else '六级'
  cards.append({'word':word,'meaning':source['meaning'],'phonetic':source['phonetic'],'hint':'','example':'',
                'frequencyRank':rank,'level':kind.upper(),'examMonthCount':len(ev['months']),'examPaperCount':len(ev['papers']),
                'studyTier':'阅读高频' if high(word,kind) else '词表增量',
                'frequencyNote':f'{label}阅读样本 · {len(ev["months"])} 个考期' if ev['months'] else ''})
 package={'formatVersion':1,'id':identity,'name':name,'version':'2026.10.31',
          'description':description+' 统计只覆盖所收录阅读文章，不等于未来必考词；不同词表有分级差异。',
          'source':'ECDICT 分级与释义；高中课标及补充词表排除；懒笔记公开真题阅读样本。详见 data/sources/provenance.json。',
          'license':'ECDICT: MIT（licenses/ECDICT-MIT.txt）；来源与统计见 THIRD_PARTY_NOTICES.md。','words':cards}
 path=out/(identity+'.bank.json');path.write_text(json.dumps(package,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
 summary.append({'id':identity,'name':name,'count':len(words),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
assert not pure4&pure6
(DATA/'catalog-summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
print(json.dumps(summary,ensure_ascii=False,indent=2))
