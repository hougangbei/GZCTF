import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { ImportHistory } from '@Components/learning/ImportHistory'
import { Role } from '@Api'

export default function History() {
  return (
    <WithRole requiredRole={Role.User}>
      <WithNavBar minWidth={0}>
        <ImportHistory />
      </WithNavBar>
    </WithRole>
  )
}
