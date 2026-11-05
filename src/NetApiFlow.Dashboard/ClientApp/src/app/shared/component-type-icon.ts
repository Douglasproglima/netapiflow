import { ComponentType } from '../core/models';

/**
 * Which geometric glyph `flow-graph.component.html` draws for a node's `ComponentType`
 * (D-31): a node already shows a state icon (color + icon + text, seção 7.2 do
 * prompt); this adds a second, distinct icon for what kind of component it is, reusing the
 * `ComponentType` mapping `core/models.ts` already freezes instead of a new contract.
 */
export type ComponentTypeIcon =
  | 'endpoint'
  | 'controller'
  | 'use-case'
  | 'service'
  | 'repository'
  | 'database'
  | 'http'
  | 'custom';

const ICON_BY_TYPE: Readonly<Record<ComponentType, ComponentTypeIcon>> = {
  Endpoint: 'endpoint',
  Controller: 'controller',
  UseCase: 'use-case',
  Service: 'service',
  Repository: 'repository',
  Database: 'database',
  Http: 'http',
  Custom: 'custom',
};

export function componentTypeIcon(type: ComponentType): ComponentTypeIcon {
  return ICON_BY_TYPE[type];
}
