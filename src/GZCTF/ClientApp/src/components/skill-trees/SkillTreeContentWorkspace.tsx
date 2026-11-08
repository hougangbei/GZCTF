import { Center, Loader, Modal, Stack, Text } from '@mantine/core'
import { useTranslation } from 'react-i18next'
import { Navigate, useNavigate, useParams } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import { ChallengeWorkspace } from '@Components/learning/ChallengeWorkspace'
import { LessonReader } from '@Components/learning/LessonReader'
import surface from '@Components/skill-trees/TrainingSurface.module.css'
import { orderedSkills, skillHref } from '@Utils/skillHierarchy'
import { useSkillTree } from '@Hooks/useSkillTrees'
import { SkillHierarchyBrowser } from './SkillHierarchyBrowser'
import classes from './SkillTreeContentWorkspace.module.css'

export const SkillTreeContentWorkspace = () => {
  const { id: treeId, categoryId, skillId, kind, contentId } = useParams()
  const navigate = useNavigate()
  const { t } = useTranslation('skillTrees')
  const { data: tree, error } = useSkillTree(treeId)

  if (kind !== 'challenge' && kind !== 'lesson') return <Navigate to="/404" replace />

  if (!tree && !error) {
    return (
      <WithNavBar className={surface.surface} minWidth={0}>
        <Center h="60vh">
          <Loader />
        </Center>
      </WithNavBar>
    )
  }

  if (error || !tree) {
    return (
      <WithNavBar className={surface.surface} minWidth={0}>
        <Center h="60vh">
          <Text c="red">{t('errors.generic')}</Text>
        </Center>
      </WithNavBar>
    )
  }

  const category = tree.categories?.find((item) => item.categoryId === categoryId)
  const skill = orderedSkills(category).find((item) =>
    skillId
      ? item.skillId === skillId
      : item.contents.some((content) => content.kind === kind && content.contentId === contentId)
  )
  const content = skill?.contents.find((item) => item.kind === kind && item.contentId === contentId)
  if (!category || !skill || !content) return <Navigate to="/404" replace />

  const closeHref = skillHref(treeId!, categoryId!, skill.skillId)
  if (!skillId) return <Navigate replace to={`${closeHref}/${kind}/${contentId}`} />
  if (kind === 'lesson')
    return (
      <WithNavBar minWidth={0}>
        <LessonReader
          key={`${categoryId}-${contentId}`}
          tree={tree}
          categoryId={categoryId!}
          lessonId={contentId!}
          skillId={skillId}
        />
      </WithNavBar>
    )
  return (
    <WithNavBar className={surface.surface} minWidth={0}>
      <Stack gap="lg">
        <SkillHierarchyBrowser tree={tree} categoryId={categoryId} skillId={skillId} />
        <Modal
          opened
          onClose={() => navigate(closeHref)}
          title={content.title}
          size={kind === 'challenge' ? 860 : 'lg'}
          centered
          classNames={
            kind === 'challenge'
              ? { content: classes.content, header: classes.header, title: classes.title, body: classes.challengeBody }
              : { content: classes.content, body: classes.body }
          }
        >
          <ChallengeWorkspace challengeId={contentId!} courseId={treeId} modal />
        </Modal>
      </Stack>
    </WithNavBar>
  )
}
