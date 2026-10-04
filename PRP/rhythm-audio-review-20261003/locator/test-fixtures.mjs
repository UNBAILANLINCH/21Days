// 固定的迁移回归样例；不读取实际研究候选或浏览器草稿，也不代表音乐对齐。
// 现有候选校验器可复用，历史研究文件不属于测试依赖，故按只读来源生成小样例。
import {clone,differences,FORMAT,VERSION,validateCandidate} from './model.mjs';
export function legacyCandidateFixture(source){
  const chart=clone(source.chart);
  chart.notes[0].timeMs+=10;
  const bundle={
    format:FORMAT,version:VERSION,status:'unapproved-candidate',createdAt:new Date().toISOString(),
    source:Object.fromEntries(['chartPath','audioPath','chartSha256','audioSha256'].map(key=>[key,source[key]])),
    review:{voice:'melody',reason:'固定迁移回归样例：只验证理由与未批准状态保留，不表示音乐对齐或声部确认。'},
    timing:{domain:'decoded-project-mp3',songStartSeconds:16,clipDurationSeconds:40,decoderCorrectionMs:0},
    chart,differences:differences(source.chart,chart),
  };
  return validateCandidate(bundle,source);
}
