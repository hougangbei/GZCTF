import { useParams, useSearchParams } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { CourseWorkspace } from '@Components/admin/course-authoring/CourseWorkspace'
import LegacySkillTreeEditor from '@Components/admin/skill-trees/LegacySkillTreeEditor'
import { Role } from '@Api'
import { useUser } from '@Hooks/useUser'

export default function CourseAuthoringPage() {
  const { id } = useParams()
  const { user } = useUser()
  const [params] = useSearchParams()
  if (params.get('legacy') === '1') return <LegacySkillTreeEditor />
  return (
    <WithRole requiredRole={Role.Admin}>
      <WithNavBar minWidth={0}>{id && <CourseWorkspace key={`${user?.userId ?? 'pending'}:${id}`} id={id} />}</WithNavBar>
    </WithRole>
  )
}
