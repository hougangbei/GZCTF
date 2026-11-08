import {expect,test,type Page} from '@playwright/test'
async function setup(page:Page){
 await page.addInitScript(()=>localStorage.setItem('language',JSON.stringify('zh-CN')))
 await page.route('**/api/**',route=>route.fulfill({json:[]}))
 await page.route('**/api/account/profile',route=>route.fulfill({json:{userId:'admin',userName:'Admin',role:'Admin'}}))
 await page.route('**/api/config',route=>route.fulfill({json:{title:'NYNUCTF'}}))
}
test('ZIP preview precedes import and result links to draft',async({page})=>{
 await setup(page);let posted=false
 await page.route('**/api/admin/platform-imports/preview',route=>route.fulfill({json:{format:'GZCTF-GAME',fingerprint:'filehash',planFingerprint:'plan',title:'比赛导入',challengeCount:26,fileCount:19,userCount:0,teamCount:0,submissionCount:0,awardCount:0,issues:[{severity:'warning',code:'no_accounts_in_game_export',sourceId:'',message:'比赛包不包含账号'}],canImport:true}}))
 await page.route('**/api/admin/platform-imports',route=>{if(route.request().method()==='POST'){posted=true;expect(route.request().postDataBuffer()?.toString()).toContain('plan');return route.fulfill({json:{id:'batch',treeId:'tree',title:'比赛导入',challengeCount:26,userCount:0}})}return route.fulfill({json:[]})})
 await page.goto('/admin/imports')
 await page.getByLabel('导出 ZIP').setInputFiles({name:'export.zip',mimeType:'application/zip',buffer:Buffer.from('test')})
 await page.getByRole('textbox',{name:'来源标识',exact:true}).fill('campus-gzctf')
 await page.getByRole('button',{name:'检查导出包',exact:true}).click()
 await expect(page.getByText('26 道题目',{exact:true})).toBeVisible()
 expect(posted).toBe(false)
 await page.getByRole('button',{name:'确认导入',exact:true}).click()
 await expect(page.getByRole('link',{name:'查看导入的路线草稿',exact:true})).toHaveAttribute('href','/admin/skill-trees/tree')
})
test('blocking preflight issues prevent import and modified options invalidate preview',async({page})=>{
 await setup(page)
 await page.route('**/api/admin/platform-imports/preview',route=>route.fulfill({json:{format:'CTFd',fingerprint:'filehash',planFingerprint:'plan',title:'CTFd',challengeCount:1,fileCount:0,userCount:1,teamCount:0,submissionCount:0,awardCount:0,issues:[{severity:'error',code:'duplicate_user_name',sourceId:'1',message:'用户名冲突'}],canImport:false}}))
 await page.goto('/admin/imports');await page.getByLabel('导出 ZIP').setInputFiles({name:'ctfd.zip',mimeType:'application/zip',buffer:Buffer.from('test')});await page.getByRole('textbox',{name:'来源标识',exact:true}).fill('ctfd')
 await page.getByRole('button',{name:'检查导出包',exact:true}).click();await expect(page.getByText('用户名冲突',{exact:true})).toBeVisible();await expect(page.getByRole('button',{name:'确认导入',exact:true})).toBeDisabled()
 await page.getByRole('textbox',{name:'来源标识',exact:true}).fill('new-source');await expect(page.getByRole('button',{name:'确认导入',exact:true})).toHaveCount(0)
})
test('personal history displays team attribution and roster without implying training credit',async({page})=>{
 await setup(page);await page.route('**/api/account/import-history?*',route=>route.fulfill({json:{batches:[{id:'batch',sourceInstance:'campus',title:'原比赛',format:'CTFd'}],members:[{id:'member',batchId:'batch',sourceUserId:'1',sourceUserName:'原账号',sourceTeamName:'原队伍'}],memberCount:1,submissions:[{id:'submit',batchId:'batch',sourceTeamName:'原队伍',challengeTitle:'旧题目',result:'correct',score:null,attribution:'team'}],awards:[],submissionCount:1,awardCount:0,skip:0,take:50}}))
 await page.setViewportSize({width:390,height:844});await page.goto('/account/history')
 await expect(page.getByRole('heading',{name:'我的比赛历史',exact:true})).toBeVisible();await expect(page.getByRole('cell',{name:'原账号',exact:true})).toBeVisible();await expect(page.getByText('队伍：原队伍',{exact:true})).toBeVisible();await expect(page.getByText('未提供',{exact:true}).first()).toBeVisible()
 expect(await page.evaluate(()=>document.documentElement.scrollWidth-innerWidth)).toBeLessThanOrEqual(1)
})
test('import form and blocking report fit a phone without horizontal overflow',async({page})=>{
 await setup(page);await page.setViewportSize({width:320,height:844});await page.goto('/admin/imports')
 await expect(page.getByRole('textbox',{name:'来源标识',exact:true})).toBeVisible();expect(await page.evaluate(()=>document.documentElement.scrollWidth-innerWidth)).toBeLessThanOrEqual(1)
})
test('imported challenge exposes pool and supplementary download links separately',async({page})=>{
 await setup(page)
 const contents=[{kind:'challenge',contentId:'challenge',title:'导入动态附件',sortOrder:0}]
 await page.route('**/api/skill-trees/tree',route=>route.fulfill({json:{skillTreeId:'tree',name:'导入路线',categories:[{categoryId:'category',name:'Misc',contents,skills:[{skillId:'skill',name:'附件技能',sortOrder:0,contents,challengeCount:1,lessonCount:0}]}]}}))
 await page.route('**/api/my-learning/skill-trees/tree',route=>route.fulfill({json:{totalCount:1,completedCount:0,completedLessonIds:[],solvedChallengeIds:[],categories:[],skills:[]}}))
 await page.route('**/api/challenges/challenge?*',route=>route.fulfill({json:{id:'challenge',title:'导入动态附件',body:'内容',type:'DynamicAttachment',ctfCategory:'Misc',hasAttachment:true,hasSupplementaryAttachment:true,hasContainer:false,hintLocaleCount:0}}))
 await page.goto('/skill-trees/tree/category/skills/skill/challenge/challenge')
 await expect(page.getByRole('link',{name:'补充附件',exact:true})).toHaveAttribute('href','/api/challenges/challenge/attachment?supplementary=true&course=tree')
 await expect(page.getByRole('link',{name:'下载附件',exact:true})).toHaveAttribute('href','/api/challenges/challenge/attachment?course=tree')
})
test('account conflict is only skipped after explicit choice and a fresh preview',async({page})=>{
 await setup(page);let skipped=false;let executed=false
 await page.route('**/api/admin/platform-imports/preview',route=>{skipped=route.request().postDataBuffer()!.toString().includes('"skipUserIds":["1"]');return route.fulfill({json:{format:'CTFd',fingerprint:'file',planFingerprint:skipped?'skip-plan':'plan',title:'CTFd',challengeCount:1,fileCount:0,userCount:1,teamCount:0,submissionCount:0,awardCount:0,issues:[{severity:skipped?'warning':'error',code:skipped?'skipped_source_user':'duplicate_user_email',sourceId:'1',message:skipped?'账号已明确跳过':'邮箱冲突'}],canImport:skipped}})})
 await page.route('**/api/admin/platform-imports',route=>{if(route.request().method()==='POST'){expect(skipped).toBe(true);expect(route.request().postDataBuffer()!.toString()).toContain('skip-plan');executed=true;return route.fulfill({json:{id:'batch',title:'CTFd',challengeCount:1,userCount:0}})}return route.fulfill({json:[]})})
 await page.goto('/admin/imports');await page.getByLabel('导出 ZIP').setInputFiles({name:'ctfd.zip',mimeType:'application/zip',buffer:Buffer.from('test')});await page.getByRole('textbox',{name:'来源标识',exact:true}).fill('ctfd');await page.getByRole('button',{name:'检查导出包',exact:true}).click()
 await expect(page.getByRole('button',{name:'确认导入',exact:true})).toBeDisabled();expect(skipped).toBe(false)
 await page.getByRole('checkbox',{name:'不创建来源账号 1，只保留历史记录',exact:true}).check();await expect(page.getByRole('button',{name:'确认导入',exact:true})).toBeDisabled();await page.getByRole('button',{name:'检查导出包',exact:true}).click();await expect(page.getByRole('button',{name:'确认导入',exact:true})).toBeEnabled();await page.getByRole('button',{name:'确认导入',exact:true}).click();await expect.poll(()=>executed).toBe(true)
})
