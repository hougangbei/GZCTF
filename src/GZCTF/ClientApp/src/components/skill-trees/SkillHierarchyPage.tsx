import { Center, Loader, Text } from '@mantine/core'
import { useParams } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import { useSkillTree } from '@Hooks/useSkillTrees'
import { SkillHierarchyBrowser } from './SkillHierarchyBrowser'
import surface from './TrainingSurface.module.css'

export const SkillHierarchyPage = () => {
  const { id, categoryId, skillId } = useParams()
  const { data: tree, error } = useSkillTree(id)
  return (
    <WithNavBar className={surface.surface} minWidth={0} withFooter>
      {tree ? (
        <SkillHierarchyBrowser tree={tree} categoryId={categoryId} skillId={skillId} />
      ) : (
        <Center h="60vh">{error ? <Text role="alert">内容暂时无法加载，请刷新重试。</Text> : <Loader />}</Center>
      )}
    </WithNavBar>
  )
}
