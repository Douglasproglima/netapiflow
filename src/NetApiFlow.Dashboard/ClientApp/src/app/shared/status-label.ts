import { FlowStatus } from '../core/models';
import { TranslationKey } from '../core/i18n/translations';

const STATUS_LABEL_KEYS: Record<FlowStatus, TranslationKey> = {
  Running: 'status.running',
  Success: 'status.success',
  Error: 'status.error',
  Cancelled: 'status.cancelled',
  Timeout: 'status.timeout',
};

export function statusLabelKey(status: FlowStatus): TranslationKey {
  return STATUS_LABEL_KEYS[status];
}
