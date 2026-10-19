export function sameCharacter(a:any,b:any){return !!a&&!!b&&a.region===b.region&&String(a.serverId)===String(b.serverId)&&a.characterId===b.characterId;}
export function characterScope(ref:any){return ref?`${ref.region}:${ref.serverId}:${ref.characterId}`:'';}
export function characterBuildKey(cls:string,ref:any){return `build:${cls}${ref?':'+characterScope(ref):''}`;}
export const personalViews=new Set(['equipment','daevanion','roadmap','planner','history','pets']);
